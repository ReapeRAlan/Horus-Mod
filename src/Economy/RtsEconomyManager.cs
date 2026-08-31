using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using HorusMod.Shared;
using HorusMod.Data;
#if HORUS_CLIENT
using HorusMod.Client;
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
                    long configLength = new System.IO.FileInfo(ConfigPath).Length;
                    if (configLength < 0 || configLength > HorusEconomyPolicy.MaxConfigFileBytes)
                        throw new InvalidDataException($"RTS economy config exceeds {HorusEconomyPolicy.MaxConfigFileBytes} bytes");
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
            if (!HorusEconomyPolicy.IsValidTickSeconds(config.incomeTickSeconds))
            {
                config.incomeTickSeconds = defaults.incomeTickSeconds;
                changed = true;
            }
            if (!HorusEconomyPolicy.IsValidMultiplier(config.unitCostMultiplier))
            {
                config.unitCostMultiplier = defaults.unitCostMultiplier;
                changed = true;
            }

            var normalizedBudgets = new List<FactionBudgetEntry>();
            var budgetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config.factionBudgets != null)
            {
                foreach (FactionBudgetEntry entry in config.factionBudgets)
                {
                    if (normalizedBudgets.Count >= HorusEconomyPolicy.MaxConfigEntries || entry == null ||
                        string.IsNullOrWhiteSpace(entry.factionName) || !HorusWireText.IsStableKey(entry.factionName) ||
                        !HorusEconomyPolicy.IsValidBudget(entry.startingBudget) || !HorusEconomyPolicy.IsValidIncome(entry.incomePerTick) ||
                        !HorusEconomyPolicy.IsValidUnitCap(entry.unitCap) || !budgetNames.Add(entry.factionName))
                    {
                        changed = true;
                        continue;
                    }
                    normalizedBudgets.Add(entry);
                }
            }
            if (normalizedBudgets.Count == 0)
            {
                config.factionBudgets = defaults.factionBudgets;
                changed = true;
            }
            else if (config.factionBudgets == null || normalizedBudgets.Count != config.factionBudgets.Count)
            {
                config.factionBudgets = normalizedBudgets;
            }

            if (config.categoryCosts == null)
            {
                config.categoryCosts = new List<CategoryCostEntry>();
                changed = true;
            }
            else
            {
                var normalizedCategories = new List<CategoryCostEntry>();
                var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (CategoryCostEntry entry in config.categoryCosts)
                {
                    if (normalizedCategories.Count >= HorusEconomyPolicy.MaxConfigEntries || entry == null ||
                        string.IsNullOrWhiteSpace(entry.category) || !HorusWireText.IsStableKey(entry.category) ||
                        !HorusEconomyPolicy.IsValidUnitCost(entry.fallbackCost) || !categoryNames.Add(entry.category))
                    {
                        changed = true;
                        continue;
                    }
                    normalizedCategories.Add(entry);
                }
                if (normalizedCategories.Count != config.categoryCosts.Count) config.categoryCosts = normalizedCategories;
            }

            var normalizedOverrides = new List<UnitCostOverride>();
            var overrideKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config.unitCostOverrides != null)
            {
                foreach (UnitCostOverride entry in config.unitCostOverrides)
                {
                    if (normalizedOverrides.Count >= HorusEconomyPolicy.MaxConfigEntries || entry == null ||
                        string.IsNullOrWhiteSpace(entry.jsonKey) || !HorusWireText.IsStableKey(entry.jsonKey) ||
                        !HorusEconomyPolicy.IsValidUnitCost(entry.cost) || !overrideKeys.Add(entry.jsonKey))
                    {
                        changed = true;
                        continue;
                    }
                    normalizedOverrides.Add(entry);
                }
            }
            if (config.unitCostOverrides == null || normalizedOverrides.Count != config.unitCostOverrides.Count)
            {
                config.unitCostOverrides = normalizedOverrides;
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
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.jsonKey) && HorusEconomyPolicy.IsValidUnitCost(entry.cost))
                        unitCostLookup[entry.jsonKey] = entry.cost;
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
            return Newtonsoft.Json.JsonConvert.DeserializeObject<RtsEconomyConfig>(json,
                new Newtonsoft.Json.JsonSerializerSettings { MaxDepth = 32 });
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
                    Budget = budgetEntry != null && HorusEconomyPolicy.IsValidBudget(budgetEntry.startingBudget) ? budgetEntry.startingBudget : 5000f,
                    IncomePerTick = budgetEntry != null && HorusEconomyPolicy.IsValidIncome(budgetEntry.incomePerTick) ? budgetEntry.incomePerTick : 50f,
                    UnitCap = budgetEntry != null && HorusEconomyPolicy.IsValidUnitCap(budgetEntry.unitCap) ? budgetEntry.unitCap : 30,
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
                    if (HorusEconomyPolicy.TryAddBudget(kvp.Value.Budget, kvp.Value.IncomePerTick, out float nextBudget))
                        kvp.Value.Budget = nextBudget;
                    else if (HorusEconomyPolicy.IsValidBudget(kvp.Value.Budget) && HorusEconomyPolicy.IsValidIncome(kvp.Value.IncomePerTick))
                        kvp.Value.Budget = HorusEconomyPolicy.MaxBudget;
                    else
                    {
                        HorusLog.Error("Economy", $"[RTS Economy] Invalid runtime economy state for '{kvp.Value.FactionName}'. Income was suspended.");
                        kvp.Value.Budget = HorusEconomyPolicy.IsValidBudget(kvp.Value.Budget) ? kvp.Value.Budget : 0f;
                        kvp.Value.IncomePerTick = 0f;
                    }
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
            if (def == null) return float.NaN;

            if (!string.IsNullOrEmpty(def.jsonKey) && unitCostLookup.TryGetValue(def.jsonKey, out float keyCost))
                return HorusEconomyPolicy.IsValidUnitCost(keyCost) ? keyCost : float.NaN;

            float multiplier = config != null && HorusEconomyPolicy.IsValidMultiplier(config.unitCostMultiplier)
                ? config.unitCostMultiplier
                : 1f;
            if (!HorusEconomyPolicy.IsValidUnitCost(def.value)) return float.NaN;
            double resolved = (double)def.value * multiplier;
            return resolved <= HorusEconomyPolicy.MaxUnitCost ? (float)resolved : float.NaN;
        }

        public float GetGroupTotalCost(List<UnitDefinition> defs)
        {
            if (defs == null || defs.Count == 0 || defs.Count > HorusProtocol.MaxEntitiesPerCommand) return float.NaN;
            float total = 0f;
            foreach (var def in defs)
                if (def == null || !HorusEconomyPolicy.TryAddUnitCost(total, GetUnitCost(def), out total)) return float.NaN;
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
                if (field != null)
                {
                    float value = Convert.ToSingle(field.GetValue(faction));
                    return HorusEconomyPolicy.IsValidBudget(value) ? value : (float?)null;
                }
                var prop = typeof(Faction).GetProperty("budget", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    float value = Convert.ToSingle(prop.GetValue(faction));
                    return HorusEconomyPolicy.IsValidBudget(value) ? value : (float?)null;
                }
                
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
            if (faction == null || !HorusEconomyPolicy.IsValidBudget(value)) return;
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
            if (!HorusEconomyPolicy.IsValidBudget(value)) return;
            if (HorusPlugin.SyncWithFactionBudget != null && HorusPlugin.SyncWithFactionBudget.Value)
            {
                SetFactionRealBudget(GetGameFaction(factionIndex), value);
            }
            var state = GetFactionState(factionIndex);
            if (state != null) state.Budget = value;
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
            if (HorusEconomyPolicy.TryAddBudget(current, delta, out float next)) SetBudget(factionIndex, next);
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
            
            long nextCap = (long)state.UnitCap + delta;
            if (nextCap < 0 || nextCap > HorusEconomyPolicy.MaxUnitCap) return;
            state.UnitCap = (int)nextCap;
            
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
                if (def == null)
                {
                    tx.IsValid = false;
                    tx.DenialReason = "Unit definition is unavailable.";
                    return tx;
                }
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
                if (defs == null || defs.Count == 0 || defs.Count > HorusProtocol.MaxEntitiesPerCommand || defs.Any(def => def == null))
                {
                    tx.IsValid = false;
                    tx.DenialReason = "Unit group is empty, unavailable, or oversized.";
                    return tx;
                }
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
            if (tx == null || !HorusEconomyPolicy.IsValidUnitCost(cost) || unitCount < 1 || unitCount > HorusProtocol.MaxEntitiesPerCommand)
            {
                if (tx != null)
                {
                    tx.IsValid = false;
                    tx.DenialReason = "Transaction cost or unit count is invalid.";
                }
                return;
            }
            var state = GetFactionState(factionIndex);
            if (state == null)
            {
                tx.IsValid = false;
                tx.DenialReason = "Faction economy not initialized.";
                return;
            }
            if (!HorusEconomyPolicy.IsValidBudget(state.Budget) || !HorusEconomyPolicy.IsValidIncome(state.IncomePerTick) ||
                !HorusEconomyPolicy.IsValidUnitCap(state.UnitCap) || state.ActiveUnitCount < 0)
            {
                tx.IsValid = false;
                tx.DenialReason = "Faction economy state is invalid.";
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

            if (spawnedUnit == null || spawnedUnit.definition == null || !UnitMatchesFaction(spawnedUnit, tx.FactionIndex))
            {
                HorusLog.Error("Economy", "[RTS Economy] Transaction commit rejected because the spawned unit or faction is invalid.");
                return;
            }
            float cost = GetUnitCost(spawnedUnit.definition);
            if (!HorusEconomyPolicy.TryAddBudget(state.Budget, -cost, out float nextBudget))
            {
                HorusLog.Error("Economy", "[RTS Economy] Transaction commit rejected because its authoritative cost or budget changed.");
                return;
            }
            state.Budget = nextBudget;

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

            if (spawnedUnits == null || spawnedUnits.Count == 0 || spawnedUnits.Count > HorusProtocol.MaxEntitiesPerCommand) return;
            float committedCost = 0f;
            if (spawnedUnits != null)
            {
                for (int i = 0; i < spawnedUnits.Count; i++)
                {
                    if (spawnedUnits[i]?.definition == null || !UnitMatchesFaction(spawnedUnits[i], tx.FactionIndex) || !HorusEconomyPolicy.TryAddUnitCost(committedCost, GetUnitCost(spawnedUnits[i].definition), out committedCost))
                    {
                        HorusLog.Error("Economy", "[RTS Economy] Group transaction commit rejected because an authoritative unit cost is invalid.");
                        return;
                    }
                }
            }
            if (!HorusEconomyPolicy.TryAddBudget(state.Budget, -committedCost, out float nextBudget))
            {
                HorusLog.Error("Economy", "[RTS Economy] Group transaction commit rejected because the authoritative budget changed.");
                return;
            }
            state.Budget = nextBudget;

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

        private static bool UnitMatchesFaction(Unit unit, int factionIndex)
        {
            if (unit == null) return false;
            FactionSlot expected = FactionSlot.Resolve(factionIndex);
            FactionHQ actual = unit.NetworkHQ ?? unit.MapHQ ?? unit.Editor_HQ;
            return expected.IsValid && actual == expected.HQ;
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
