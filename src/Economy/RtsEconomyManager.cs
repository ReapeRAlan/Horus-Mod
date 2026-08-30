using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if HORUS_CLIENT
using HorusMod.Client;
using HorusMod.Shared;
#endif
using HorusMod.Networking;
using HorusMod.Logging;

namespace HorusMod.Economy
{
    /// <summary>
    /// Central RTS economy controller. Handles config loading/saving, budget tracking,
    /// income ticks, unit cap enforcement, and transaction validation.
    /// Only active when <see cref="CurrentMode"/> is <see cref="HorusMode.RtsCommander"/>.
    /// </summary>
    public class RtsEconomyManager
    {
        // ─── Singleton ───────────────────────────────────────────────────────────
        public static RtsEconomyManager Instance { get; private set; }

        // ─── Current Mode ────────────────────────────────────────────────────────
        private HorusMode currentMode = HorusMode.Sandbox;
        private RtsDeployMode deployMode = RtsDeployMode.FreePlacementPaid;
        public HorusMode CurrentMode
        {
            get => currentMode;
            set
            {
#if HORUS_CLIENT
                if (HorusRemoteAuthority.IsRemoteSession && !HorusClientTransport.ApplyingSnapshot)
                {
                    HorusRemoteAuthority.TrySubmit(HorusCommandKind.SetRtsMode,new HorusCommandPayload{IntValue=(int)value});
                    return;
                }
#endif
                currentMode = value;
            }
        }
        public RtsDeployMode DeployMode
        {
            get => deployMode;
            set
            {
#if HORUS_CLIENT
                if (HorusRemoteAuthority.IsRemoteSession && !HorusClientTransport.ApplyingSnapshot)
                {
                    HorusRemoteAuthority.TrySubmit(HorusCommandKind.SetRtsDeployMode,new HorusCommandPayload{IntValue=(int)value});
                    return;
                }
#endif
                deployMode = value;
            }
        }

        // ─── Config ──────────────────────────────────────────────────────────────
        private RtsEconomyConfig config;
        private readonly Dictionary<string, float> unitCostLookup = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // ─── Runtime State ───────────────────────────────────────────────────────
        private readonly Dictionary<int, FactionEconomyState> factionStates = new Dictionary<int, FactionEconomyState>();
        private float lastIncomeTickTime;
        private bool matchInitialized;

        // ─── Deployment Confirmation State ───────────────────────────────────────
        public bool IsDeploymentArmed { get; private set; }
        public UnitDefinition ArmedDefinition { get; private set; }
        public List<UnitDefinition> ArmedGroupDefinitions { get; private set; }
        public float ArmedCost { get; private set; }
        public string ArmedStatusText { get; private set; } = "";

        // ─── Config Path ────────────────────────────────────────────────────────
        private static string ConfigDir => System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "HorusMod");
        private static string ConfigPath => System.IO.Path.Combine(ConfigDir, "rts_economy.json");

        // ─── Constructor / Init ──────────────────────────────────────────────────
        public RtsEconomyManager()
        {
            Instance = this;
            LoadOrCreateConfig();
            new RtsFactoryManager();
        }

        // ─── Config Load/Save ────────────────────────────────────────────────────

        public void LoadOrCreateConfig()
        {
            try
            {
                System.IO.Directory.CreateDirectory(ConfigDir);

                if (!System.IO.File.Exists(ConfigPath))
                {
                    config = CreateDefaultConfig();
                    string json = SerializeConfig(config);
                    System.IO.File.WriteAllText(ConfigPath, json);
                    HorusLog.Info("Economy", $"[RTS Economy] Created default config at {ConfigPath}");
                }
                else
                {
                    string json = System.IO.File.ReadAllText(ConfigPath);
                    config = DeserializeConfig(json);
                    if (config == null) throw new Exception("Parsed config is null");
                    HorusLog.Info("Economy", $"[RTS Economy] Loaded config from {ConfigPath}");
                }

                if (NormalizeConfig())
                {
                    SaveConfig();
                    HorusLog.Info("Economy", "[RTS Economy] Migrated incomplete config to the current schema.");
                }

                // Build lookup tables
                RebuildLookups();
            }
            catch (Exception ex)
            {
                HorusLog.Error("Economy", $"[RTS Economy] Config load failed: {ex.Message}. Using defaults.");
                config = CreateDefaultConfig();
                SaveConfig();
                RebuildLookups();
            }
        }

        private bool NormalizeConfig()
        {
            if (config == null)
            {
                config = CreateDefaultConfig();
                return true;
            }

            bool changed = false;
            RtsEconomyConfig defaults = CreateDefaultConfig();
            if (config.incomeTickSeconds < 1f)
            {
                config.incomeTickSeconds = defaults.incomeTickSeconds;
                changed = true;
            }
            if (config.unitCostMultiplier <= 0f)
            {
                config.unitCostMultiplier = defaults.unitCostMultiplier;
                changed = true;
            }
            if (config.factionBudgets == null || config.factionBudgets.Count == 0)
            {
                config.factionBudgets = defaults.factionBudgets;
                changed = true;
            }
            if (config.categoryCosts == null)
            {
                config.categoryCosts = new List<CategoryCostEntry>();
                changed = true;
            }
            if (config.unitCostOverrides == null)
            {
                config.unitCostOverrides = new List<UnitCostOverride>();
                changed = true;
            }
            return changed;
        }

        private void RebuildLookups()
        {
            unitCostLookup.Clear();
            if (config.unitCostOverrides != null)
            {
                foreach (var entry in config.unitCostOverrides)
                {
                    string key = !string.IsNullOrWhiteSpace(entry.jsonKey) ? entry.jsonKey : entry.unitName;
                    if (!string.IsNullOrWhiteSpace(key))
                        unitCostLookup[key] = entry.cost;
                }
            }

        }

        public void SaveConfig()
        {
            try
            {
                if (config != null)
                {
                    string json = SerializeConfig(config);
                    System.IO.File.WriteAllText(ConfigPath, json);
                }
            }
            catch (Exception ex)
            {
                HorusLog.Error("Economy", $"[RTS Economy] Config save failed: {ex.Message}");
            }
        }

        private static string SerializeConfig(RtsEconomyConfig value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented);
        }

        private static RtsEconomyConfig DeserializeConfig(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<RtsEconomyConfig>(json);
        }

        private static RtsEconomyConfig CreateDefaultConfig()
        {
            var cfg = new RtsEconomyConfig
            {
                incomeTickSeconds = 5f,
                unitCostMultiplier = 1f,
                factionBudgets = new List<FactionBudgetEntry>
                {
                    new FactionBudgetEntry { factionName = "Primeva", startingBudget = 10000f, incomePerTick = 5f, unitCap = 30 },
                    new FactionBudgetEntry { factionName = "Boscali", startingBudget = 10000f, incomePerTick = 5f, unitCap = 30 }
                },
                categoryCosts = new List<CategoryCostEntry>(),
                unitCostOverrides = new List<UnitCostOverride>()
            };
            return cfg;
        }

        // ─── Match Lifecycle ─────────────────────────────────────────────────────

        /// <summary>
        /// Initializes faction economy states from the config. Call once when switching to RTS mode
        /// or at the start of a match.
        /// </summary>
        public void InitializeMatch()
        {
            factionStates.Clear();
            var factions = FactionRegistry.factions;
            if (factions == null) return;

            for (int i = 0; i < factions.Count; i++)
            {
                string fname = factions[i].factionName ?? $"Faction{i}";
                var budgetEntry = config.factionBudgets?.FirstOrDefault(
                    b => fname.StartsWith(b.factionName, StringComparison.OrdinalIgnoreCase));

                var state = new FactionEconomyState
                {
                    FactionName = fname,
                    Budget = budgetEntry?.startingBudget ?? 5000f,
                    IncomePerTick = budgetEntry?.incomePerTick ?? 50f,
                    UnitCap = budgetEntry?.unitCap ?? 30,
                    ActiveUnitCount = 0
                };
                factionStates[i] = state;
                HorusLog.Info("Economy", $"[RTS Economy] Init faction '{fname}': budget={state.Budget}, income={state.IncomePerTick}/tick, cap={state.UnitCap}");
            }

            lastIncomeTickTime = Time.time;
            matchInitialized = true;
            RtsFactoryManager.Instance?.InitializeMatchFactories();
        }

        /// <summary>
        /// Resets economy state when switching back to Sandbox or ending a match.
        /// </summary>
        public void ResetMatch()
        {
            factionStates.Clear();
            matchInitialized = false;
            DisarmDeployment();
            RtsFactoryManager.Instance?.ResetMatchFactories();
        }

        /// <summary>
        /// Fully clears all runtime state during a mission unload.
        /// </summary>
        public void ResetRuntimeState()
        {
            ResetMatch();
            // Additionally clear any dangling instance-level refs if needed
        }

        // ─── Tick (called from HorusManager.Update) ─────────────────────────────

        public void Tick()
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession)
            {
                if (HorusPermissions.InMission() && !matchInitialized) InitializeMatch();
                return;
            }
#endif
            if (CurrentMode != HorusMode.RtsCommander) return;

            // If we are not in a mission, reset match economy and return
            if (!HorusPermissions.InMission())
            {
                if (matchInitialized) ResetMatch();
                return;
            }

            if (!matchInitialized)
            {
                InitializeMatch();
            }

            // Income ticks
            bool incomeEnabled = HorusPlugin.EnableRtsIncome != null && HorusPlugin.EnableRtsIncome.Value;
            float tickInterval = config?.incomeTickSeconds ?? 5f;
            if (tickInterval < 1f) tickInterval = 1f;

            if (incomeEnabled && Time.time - lastIncomeTickTime >= tickInterval)
            {
                lastIncomeTickTime = Time.time;
                foreach (var kvp in factionStates)
                {
                    kvp.Value.Budget += kvp.Value.IncomePerTick;
                }
            }

            // Periodic dead-unit cleanup (every 2 seconds)
            bool capsEnabled = HorusPlugin.EnableRtsUnitCap != null && HorusPlugin.EnableRtsUnitCap.Value;
            if (capsEnabled && Time.frameCount % 120 == 0)
            {
                foreach (var kvp in factionStates)
                {
                    kvp.Value.CleanDeadUnits();
                }
            }

            // Tick factories and automatic production
            RtsFactoryManager.Instance?.Tick();
        }

        // ─── Cost Resolution ─────────────────────────────────────────────────────

        public float GetUnitCost(UnitDefinition def)
        {
            if (def == null) return 0f;

            if (!string.IsNullOrEmpty(def.jsonKey) && unitCostLookup.TryGetValue(def.jsonKey, out float keyCost))
                return keyCost;

            float multiplier = config != null && config.unitCostMultiplier > 0f
                ? config.unitCostMultiplier
                : 1f;
            return Mathf.Max(0f, def.value * multiplier);
        }

        public float GetGroupTotalCost(List<UnitDefinition> defs)
        {
            if (defs == null) return 0f;
            float total = 0f;
            foreach (var def in defs) total += GetUnitCost(def);
            return total;
        }

        // ─── Faction State Access ────────────────────────────────────────────────

        private Faction GetGameFaction(int factionIndex)
        {
            var factions = FactionRegistry.factions;
            if (factions != null && factionIndex >= 0 && factionIndex < factions.Count)
            {
                return factions[factionIndex];
            }
            return null;
        }

        private float? GetFactionRealBudget(Faction faction)
        {
            if (faction == null) return null;
            try
            {
                var field = typeof(Faction).GetField("budget", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (field != null) return Convert.ToSingle(field.GetValue(faction));
                var prop = typeof(Faction).GetProperty("budget", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null) return Convert.ToSingle(prop.GetValue(faction));
                
                HorusLog.Warning("Economy", $"[RTS Economy] SyncWithFactionBudget failed: could not find 'budget' on Faction {faction.factionName}");
            }
            catch (Exception ex)
            {
                HorusLog.Warning("Economy", $"[RTS Economy] SyncWithFactionBudget exception for {faction.factionName}: {ex.Message}");
            }
            return null;
        }

        private void SetFactionRealBudget(Faction faction, float value)
        {
            if (faction == null) return;
            try
            {
                var field = typeof(Faction).GetField("budget", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (field != null) { field.SetValue(faction, Convert.ChangeType(value, field.FieldType)); return; }
                var prop = typeof(Faction).GetProperty("budget", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite) { prop.SetValue(faction, Convert.ChangeType(value, prop.PropertyType)); return; }

                HorusLog.Warning("Economy", $"[RTS Economy] SetFactionRealBudget failed: could not find writable 'budget' on Faction {faction.factionName}");
            }
            catch (Exception ex)
            {
                HorusLog.Warning("Economy", $"[RTS Economy] SetFactionRealBudget exception for {faction.factionName}: {ex.Message}");
            }
        }

        public FactionEconomyState GetFactionState(int factionIndex)
        {
            factionStates.TryGetValue(factionIndex, out var state);
            return state;
        }

        public float GetBudget(int factionIndex)
        {
            if (HorusPlugin.SyncWithFactionBudget != null && HorusPlugin.SyncWithFactionBudget.Value)
            {
                float? realBudget = GetFactionRealBudget(GetGameFaction(factionIndex));
                if (realBudget.HasValue) return realBudget.Value;
            }
            return GetFactionState(factionIndex)?.Budget ?? 0f;
        }

        public void SetBudget(int factionIndex, float value)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession && !HorusClientTransport.ApplyingSnapshot)
            {
                HorusRemoteAuthority.TrySubmit(HorusCommandKind.SetBudget,new HorusCommandPayload{FactionIndex=factionIndex,FloatValue=value});
                return;
            }
#endif
            if (HorusPlugin.SyncWithFactionBudget != null && HorusPlugin.SyncWithFactionBudget.Value)
            {
                SetFactionRealBudget(GetGameFaction(factionIndex), value);
            }
            var state = GetFactionState(factionIndex);
            if (state != null) state.Budget = Mathf.Max(0f, value);
        }

        public void AdjustBudget(int factionIndex, float delta)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession && !HorusClientTransport.ApplyingSnapshot)
            {
                HorusRemoteAuthority.TrySubmit(HorusCommandKind.AdjustBudget,new HorusCommandPayload{FactionIndex=factionIndex,FloatValue=delta});
                return;
            }
#endif
            float current = GetBudget(factionIndex);
            SetBudget(factionIndex, current + delta);
        }

        public void AdjustUnitCap(int factionIndex, int delta)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession && !HorusClientTransport.ApplyingSnapshot)
            {
                HorusRemoteAuthority.TrySubmit(HorusCommandKind.AdjustUnitCap,new HorusCommandPayload{FactionIndex=factionIndex,IntValue=delta});
                return;
            }
#endif
            var state = GetFactionState(factionIndex);
            if (state == null) return;
            
            state.UnitCap = Mathf.Max(0, state.UnitCap + delta);
            
            // update config
            if (config != null && config.factionBudgets != null)
            {
                var budgetEntry = config.factionBudgets.FirstOrDefault(
                    b => state.FactionName.StartsWith(b.factionName, StringComparison.OrdinalIgnoreCase));
                if (budgetEntry != null)
                {
                    budgetEntry.unitCap = state.UnitCap;
                    SaveConfig();
                }
            }
        }

        // ─── Transaction Pipeline ────────────────────────────────────────────────

        /// <summary>
        /// Creates and validates a purchase transaction for a single unit.
        /// Does NOT deduct funds or increment cap yet.
        /// </summary>
        public RtsTransaction CreateTransaction(UnitDefinition def, int factionIndex)
        {
            var tx = new RtsTransaction
            {
                Definition = def,
                FactionIndex = factionIndex,
                Cost = GetUnitCost(def),
                IsValid = true,
                DenialReason = ""
            };

            if (CurrentMode != HorusMode.RtsCommander)
            {
                // Sandbox mode: always valid, no cost
                tx.Cost = 0f;
                return tx;
            }

            ValidateTransaction(tx, factionIndex, tx.Cost, 1);
            return tx;
        }

        /// <summary>
        /// Creates and validates a group purchase transaction.
        /// </summary>
        public RtsTransaction CreateGroupTransaction(List<UnitDefinition> defs, int factionIndex)
        {
            float totalCost = GetGroupTotalCost(defs);
            var tx = new RtsTransaction
            {
                GroupDefinitions = defs,
                FactionIndex = factionIndex,
                GroupTotalCost = totalCost,
                Cost = totalCost,
                IsValid = true,
                DenialReason = ""
            };

            if (CurrentMode != HorusMode.RtsCommander)
            {
                tx.Cost = 0f;
                tx.GroupTotalCost = 0f;
                return tx;
            }

            // Check if group purchases are allowed
            if (HorusPlugin.AllowGroupPurchasesInRtsMode != null && !HorusPlugin.AllowGroupPurchasesInRtsMode.Value)
            {
                tx.IsValid = false;
                tx.DenialReason = "Group purchases are disabled in RTS Mode.";
                return tx;
            }

            ValidateTransaction(tx, factionIndex, totalCost, defs?.Count ?? 0);
            return tx;
        }

        private void ValidateTransaction(RtsTransaction tx, int factionIndex, float cost, int unitCount)
        {
            var state = GetFactionState(factionIndex);
            if (state == null)
            {
                tx.IsValid = false;
                tx.DenialReason = "Faction economy not initialized.";
                return;
            }

            // Budget check
            if (state.Budget < cost)
            {
                tx.IsValid = false;
                tx.DenialReason = $"Insufficient budget. Need {cost:F0}, have {state.Budget:F0}.";
                return;
            }

            // Unit cap check
            bool capsEnabled = HorusPlugin.EnableRtsUnitCap != null && HorusPlugin.EnableRtsUnitCap.Value;
            if (capsEnabled && state.UnitCap > 0)
            {
                if (state.ActiveUnitCount + unitCount > state.UnitCap)
                {
                    tx.IsValid = false;
                    tx.DenialReason = $"Unit cap reached ({state.ActiveUnitCount}/{state.UnitCap}).";
                    return;
                }
            }

            // Strict base deployment check
            if (DeployMode == RtsDeployMode.StrictBaseDeployment &&
                HorusPlugin.EnableStrictBaseDeployment != null && HorusPlugin.EnableStrictBaseDeployment.Value)
            {
                // This will be validated at spawn time by checking proximity to friendly structures
                // We set a flag here so the caller knows to perform the check
            }
        }

        /// <summary>
        /// Commits a transaction: deducts funds, increments unit count, and tracks the spawned unit.
        /// Call AFTER the unit has been successfully spawned.
        /// </summary>
        public void CommitTransaction(RtsTransaction tx, Unit spawnedUnit)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession) return;
#endif
            if (CurrentMode != HorusMode.RtsCommander) return;
            if (tx == null || !tx.IsValid) return;

            var state = GetFactionState(tx.FactionIndex);
            if (state == null) return;

            float cost = tx.IsGroupTransaction ? tx.GroupTotalCost : tx.Cost;
            state.Budget = Mathf.Max(0f, state.Budget - cost);

            if (spawnedUnit != null)
            {
                state.TrackedUnits.Add(spawnedUnit);
                state.ActiveUnitCount = state.TrackedUnits.Count;
            }

            HorusLog.Info("Economy", $"[RTS Economy] Transaction committed: -{cost:F0} for faction '{state.FactionName}'. Budget={state.Budget:F0}, Units={state.ActiveUnitCount}/{state.UnitCap}");
        }

        /// <summary>
        /// Commits a group transaction. Adds all spawned units.
        /// </summary>
        public void CommitGroupTransaction(RtsTransaction tx, List<Unit> spawnedUnits)
        {
#if HORUS_CLIENT
            if (HorusRemoteAuthority.IsRemoteSession) return;
#endif
            if (CurrentMode != HorusMode.RtsCommander) return;
            if (tx == null || !tx.IsValid) return;

            var state = GetFactionState(tx.FactionIndex);
            if (state == null) return;

            float committedCost = 0f;
            if (spawnedUnits != null)
            {
                for (int i = 0; i < spawnedUnits.Count; i++)
                    if (spawnedUnits[i]?.definition != null) committedCost += GetUnitCost(spawnedUnits[i].definition);
            }
            state.Budget = Mathf.Max(0f, state.Budget - committedCost);

            if (spawnedUnits != null)
            {
                foreach (var unit in spawnedUnits)
                {
                    if (unit != null) state.TrackedUnits.Add(unit);
                }
                state.ActiveUnitCount = state.TrackedUnits.Count;
            }

            int successfulCount = spawnedUnits?.Count ?? 0;
            HorusLog.Info("Economy", $"[RTS Economy] Group transaction committed: -{committedCost:F0} for {successfulCount} successful spawn(s) in faction '{state.FactionName}'. Budget={state.Budget:F0}, Units={state.ActiveUnitCount}/{state.UnitCap}");
        }

        // ─── Deployment Confirmation ─────────────────────────────────────────────

        /// <summary>
        /// Arms a single unit for deployment (first click / Arm button).
        /// </summary>
        public void ArmDeployment(UnitDefinition def, int factionIndex)
        {
            if (def == null) return;
            var tx = CreateTransaction(def, factionIndex);
            if (!tx.IsValid)
            {
                ArmedStatusText = "⚠ " + tx.DenialReason;
                return;
            }

            IsDeploymentArmed = true;
            ArmedDefinition = def;
            ArmedGroupDefinitions = null;
            ArmedCost = tx.Cost;
            ArmedStatusText = $"✓ ARMED: {def.unitName} ({tx.Cost:F0})";
            HorusLog.Info("Economy", $"[RTS Economy] Deployment armed: {def.unitName} cost={tx.Cost:F0}");
        }

        /// <summary>
        /// Arms a group for deployment.
        /// </summary>
        public void ArmGroupDeployment(List<UnitDefinition> defs, int factionIndex)
        {
            if (defs == null || defs.Count == 0) return;
            var tx = CreateGroupTransaction(defs, factionIndex);
            if (!tx.IsValid)
            {
                ArmedStatusText = "⚠ " + tx.DenialReason;
                return;
            }

            IsDeploymentArmed = true;
            ArmedDefinition = null;
            ArmedGroupDefinitions = new List<UnitDefinition>(defs);
            ArmedCost = tx.GroupTotalCost;
            ArmedStatusText = $"✓ ARMED: Group x{defs.Count} ({tx.GroupTotalCost:F0})";
            HorusLog.Info("Economy", $"[RTS Economy] Group deployment armed: {defs.Count} units, cost={tx.GroupTotalCost:F0}");
        }

        public void DisarmDeployment()
        {
            IsDeploymentArmed = false;
            ArmedDefinition = null;
            ArmedGroupDefinitions = null;
            ArmedCost = 0f;
            ArmedStatusText = "";
        }

        // ─── Strict Base Deployment Validation ───────────────────────────────────

        /// <summary>
        /// Returns true if the position is within BaseDeploymentRadius of a friendly building or carrier.
        /// </summary>
        public bool IsWithinBaseRange(Vector3 localPos, int factionIndex)
        {
            float radius = HorusPlugin.BaseDeploymentRadius != null ? HorusPlugin.BaseDeploymentRadius.Value : 3000f;
            var factions = FactionRegistry.factions;
            if (factions == null || factionIndex < 0 || factionIndex >= factions.Count) return false;

            Faction faction = factions[factionIndex];
            FactionHQ friendlyHQ = FactionRegistry.HQFromFaction(faction);

            if (UnitRegistry.allUnits == null) return false;

            foreach (var unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.gameObject == null || unit.disabled) continue;
                if (unit.unitState == Unit.UnitState.Destroyed) continue;

                // Check if the unit belongs to the same faction by comparing HQ
                FactionHQ unitHQ = unit.NetworkHQ ?? unit.MapHQ ?? unit.Editor_HQ;
                if (unitHQ != friendlyHQ) continue;

                // Check if it's a building or carrier ship
                bool isBase = unit is Building || (unit is Ship && unit.definition != null && unit.definition is ShipDefinition);
                if (!isBase) continue;

                float dist = Vector3.Distance(localPos, unit.transform.position);
                if (dist <= radius) return true;
            }
            return false;
        }
    }
}
