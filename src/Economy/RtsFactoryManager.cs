using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Mirage;
using HorusMod.Core;
using HorusMod.Networking;
using HorusMod.Logging;
using HorusMod.UI;
using HorusMod.Interaction;
using HorusMod.Spawning;
using HorusMod.Data;

namespace HorusMod.Economy
{
    public class RtsFactoryManager
    {
        public static RtsFactoryManager Instance { get; private set; }

        public List<RtsFactory> activeFactories = new List<RtsFactory>();
        private RtsFactoriesConfig config;
        private float incomeAccumulator;
        private float cleanupAccumulator;
        private readonly Dictionary<string, float> lastBlockLogTimes = new Dictionary<string, float>();

        private const float FactoryTickSeconds = 5f;
        private const float CleanupSeconds = 5f;
        private const float BlockLogCooldownSeconds = 10f;
        private const int TerrainLayerMask = 1 << 6;
        private static readonly RaycastHit[] FactoryGroundHitCache = new RaycastHit[32];

        private static string ConfigDir => Path.Combine(BepInEx.Paths.ConfigPath, "HorusMod");
        private static string ConfigPath => Path.Combine(ConfigDir, "rts_factories.json");
        private static string InstancesPath => Path.Combine(ConfigDir, "rts_factory_instances.json");

        public RtsFactoriesConfig Config => config;

        public RtsFactoryManager()
        {
            Instance = this;
            LoadOrCreateConfig();
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────────

        public void InitializeMatchFactories()
        {
            activeFactories.Clear();
            LoadOrCreateConfig();
            LoadInstancesInternal();

            // Log available buildings for reference
            if (Encyclopedia.i != null && Encyclopedia.i.buildings != null)
            {
                HorusLog.Verbose("Factory", "Available building definitions:");
                foreach (var b in Encyclopedia.i.buildings)
                {
                    if (b != null)
                    {
                        HorusLog.Verbose("Factory", $"Building '{b.unitName}', key '{b.jsonKey}'.");
                    }
                }
            }

            RecreateVirtualFactoryAnchors();
            AutoDetectFactories();
            incomeAccumulator = 0f;
            cleanupAccumulator = 0f;
            HorusLog.Info("Factory", "[HORUS RTS] Initialized match factories.");
        }

        public void ResetMatchFactories()
        {
            activeFactories.Clear();
            HorusLog.Info("Factory", "[HORUS RTS] Reset match factories.");
        }

        // ─── Ticking ─────────────────────────────────────────────────────────────

        public void Tick()
        {
            var economyManager = RtsEconomyManager.Instance;
            if (economyManager == null || economyManager.CurrentMode != HorusMode.RtsCommander) return;
            if (config == null || !config.settings.enableFactories) return;
            if (!HorusPermissions.CanSpawn())
            {
                LogBlocked(null, "tick-permission", "[HORUS RTS] Factory tick skipped: Host only.");
                return;
            }

            cleanupAccumulator += Time.deltaTime;
            if (cleanupAccumulator >= CleanupSeconds)
            {
                cleanupAccumulator = 0f;
                CleanupProducedUnits();
            }

            // 0. Auto-recreate virtual factory visual anchors if they are missing
            float retryNow = Time.unscaledTime;
            if (activeFactories.Any(f => f != null && f.isVirtual && f.anchorUnit == null &&
                !f.anchorDestroyed && retryNow >= f.nextAnchorRetryTime))
            {
                RecreateVirtualFactoryAnchors();
            }

            // 1. Income Tick
            if (config.settings.factoryIncomeEnabled)
            {
                incomeAccumulator += Time.deltaTime;
                if (incomeAccumulator >= FactoryTickSeconds)
                {
                    float tickSeconds = incomeAccumulator;
                    incomeAccumulator = 0f;

                    foreach (var factory in activeFactories)
                    {
                        if (factory == null) continue;
                        if (!factory.enabled)
                        {
                            LogBlocked(factory, "income-disabled", "[HORUS RTS] Factory income skipped: factory disabled");
                            continue;
                        }
                        if (!factory.generateIncome || factory.incomePerMinute <= 0f) continue;
                        if (!IsFactoryAnchorOperational(factory, true, out string anchorReason))
                        {
                            LogBlocked(factory, "income-anchor", $"[HORUS RTS] Factory income skipped: {anchorReason}");
                            continue;
                        }

                        float added = (factory.incomePerMinute / 60f) * tickSeconds;
                        economyManager.AdjustBudget(factory.factionId, added);
                        HorusLog.Trace("Factory", "Income:" + factory.id,
                            $"Income +{added:F1} {factory.factionName} from {factory.displayName}.", FactoryTickSeconds);
                    }
                }
            }

            // 2. Production Tick
            if (config.settings.factoryProductionEnabled)
            {
                foreach (var factory in activeFactories)
                {
                    ProcessFactoryProduction(factory, economyManager);
                }
            }
        }

        // ─── Anchor Recreation Logic ──────────────────────────────────────────────

        private void ProcessFactoryProduction(RtsFactory factory, RtsEconomyManager economyManager)
        {
            if (factory == null) return;
            if (!CanUseFactoryFaction(factory.factionId, out string factionReason))
            {
                factory.lastStatus = factionReason;
                LogBlocked(factory, "production-faction", $"[HORUS RTS] Factory production blocked: {factionReason}");
                return;
            }
            if (!factory.enabled)
            {
                factory.lastStatus = "Factory disabled";
                LogBlocked(factory, "production-disabled", "[HORUS RTS] Factory production skipped: factory disabled");
                return;
            }
            if (!factory.produceUnits)
            {
                factory.lastStatus = "Production disabled";
                return;
            }

            if (!IsFactoryAnchorOperational(factory, true, out string anchorReason))
            {
                factory.lastStatus = anchorReason;
                LogBlocked(factory, "production-anchor", $"[HORUS RTS] Factory production skipped: {anchorReason}");
                return;
            }

            if (factory.productionUnitKeys == null || factory.productionUnitKeys.Count == 0)
            {
                factory.lastStatus = "Production queue empty";
                LogBlocked(factory, "production-empty-queue", "[HORUS RTS] Factory production blocked: production queue empty");
                return;
            }

            CleanupProducedUnits(factory);

            if (factory.maxActiveProducedUnits > 0 && factory.activeProducedUnits.Count >= factory.maxActiveProducedUnits)
            {
                factory.lastStatus = $"Active unit cap reached ({factory.activeProducedUnits.Count}/{factory.maxActiveProducedUnits})";
                LogBlocked(factory, "production-cap", "[HORUS RTS] Factory production blocked: active unit cap reached");
                return;
            }

            if (factory.currentProductionIndex < 0 || factory.currentProductionIndex >= factory.productionUnitKeys.Count)
            {
                factory.currentProductionIndex = 0;
            }

            string nextKey = factory.productionUnitKeys[factory.currentProductionIndex];
            UnitDefinition def = ResolveProductionDefinition(factory, nextKey);
            if (def == null)
            {
                factory.lastStatus = $"Unit not found: {nextKey}";
                LogBlocked(factory, "production-missing-unit", $"[HORUS RTS] Factory production blocked: missing production unit '{nextKey}'");
                AdvanceQueue(factory);
                return;
            }

            float cost = economyManager.GetUnitCost(def);
            bool consumes = config.settings.productionConsumesBudget && factory.consumeBudgetForProduction;
            if (consumes)
            {
                float currentBudget = economyManager.GetBudget(factory.factionId);
                if (currentBudget < cost)
                {
                    factory.lastStatus = $"Insufficient budget ({currentBudget:F0}/{cost:F0})";
                    LogBlocked(factory, "production-budget", "[HORUS RTS] Factory production blocked: insufficient budget");
                    return;
                }
            }

            factory.productionTimer += Time.deltaTime;
            float interval = Mathf.Max(1f, factory.productionIntervalSeconds);
            if (factory.productionTimer < interval)
            {
                factory.lastStatus = $"Building {def.unitName}: {factory.productionTimer:F0}/{interval:F0}s";
                return;
            }

            factory.productionTimer = 0f;
            Unit spawned = SpawnFactoryUnit(factory, def);
            if (spawned == null)
            {
                factory.lastStatus = $"Spawn failed: {def.unitName}";
                HorusLog.Error("Factory", $"[HORUS RTS] Factory production failed: failed spawn for '{def.unitName}' from {factory.displayName}");
                return;
            }

            factory.activeProducedUnits.Add(spawned);

            var factionState = economyManager.GetFactionState(factory.factionId);
            if (factionState != null)
            {
                factionState.TrackedUnits.Add(spawned);
                factionState.ActiveUnitCount = factionState.TrackedUnits.Count;
            }

            if (consumes)
            {
                economyManager.AdjustBudget(factory.factionId, -cost);
            }

            float remainingBudget = economyManager.GetBudget(factory.factionId);
            factory.lastStatus = $"Produced {def.unitName}";
            HorusLog.Info("Factory", $"[HORUS RTS] Factory produced: {def.unitName} cost={cost:F0} remaining={remainingBudget:F0}");
            AdvanceQueue(factory);
        }

        private void AdvanceQueue(RtsFactory factory)
        {
            if (factory == null || factory.productionUnitKeys == null || factory.productionUnitKeys.Count == 0)
            {
                return;
            }
            factory.currentProductionIndex = (factory.currentProductionIndex + 1) % factory.productionUnitKeys.Count;
        }

        private void CleanupProducedUnits()
        {
            foreach (var factory in activeFactories)
            {
                CleanupProducedUnits(factory);
            }

            var economyManager = RtsEconomyManager.Instance;
            if (economyManager != null)
            {
                for (int i = 0; i < 16; i++)
                {
                    var state = economyManager.GetFactionState(i);
                    if (state != null) state.CleanDeadUnits();
                }
            }
        }

        private void CleanupProducedUnits(RtsFactory factory)
        {
            if (factory == null || factory.activeProducedUnits == null) return;
            int removed = factory.activeProducedUnits.RemoveAll(IsDeadUnit);
            if (removed > 0)
            {
                HorusLog.Info("Factory", $"[HORUS RTS] Factory cleanup: removed {removed} dead produced units from {factory.displayName}. Active={factory.activeProducedUnits.Count}/{factory.maxActiveProducedUnits}");
            }
        }

        private bool IsFactoryAnchorOperational(RtsFactory factory, bool markDestroyed, out string reason)
        {
            reason = "";
            if (factory == null)
            {
                reason = "factory missing";
                return false;
            }

            if (factory.anchorDestroyed)
            {
                reason = "anchor destroyed";
                return false;
            }

            if (factory.anchorUnit != null && IsDeadUnit(factory.anchorUnit))
            {
                if (markDestroyed) MarkAnchorDestroyed(factory);
                reason = "anchor destroyed";
                return false;
            }

            if (factory.isVirtual && !string.IsNullOrEmpty(factory.visualBuilding) && factory.anchorUnit == null)
            {
                reason = "anchor missing";
                return false;
            }

            if (!factory.isVirtual && !string.IsNullOrEmpty(factory.anchorUnitName) && factory.anchorUnit == null)
            {
                if (markDestroyed) MarkAnchorDestroyed(factory);
                reason = "anchor destroyed";
                return false;
            }

            return true;
        }

        private static bool IsDeadUnit(Unit unit)
        {
            return unit == null || unit.gameObject == null || unit.disabled || unit.unitState == Unit.UnitState.Destroyed;
        }

        private void MarkAnchorDestroyed(RtsFactory factory)
        {
            if (factory == null) return;
            factory.enabled = false;
            factory.anchorDestroyed = true;
            factory.anchorUnit = null;
            HorusLog.Info("Factory", $"[HORUS RTS] Factory disabled because anchor unit was destroyed: {factory.displayName}");
        }

        private void LogBlocked(RtsFactory factory, string reasonKey, string message)
        {
            string id = factory?.id ?? "global";
            string key = id + ":" + reasonKey;
            float now = Time.time;
            if (lastBlockLogTimes.TryGetValue(key, out float last) && now - last < BlockLogCooldownSeconds)
            {
                return;
            }
            lastBlockLogTimes[key] = now;
            HorusLog.Info("Factory", message);
        }

        public void RecreateVirtualFactoryAnchors()
        {
            if (!HorusPermissions.CanSpawn())
            {
                LogPermissionBlocked("recreate factory visuals");
                return;
            }
            if (Spawner.i == null || Encyclopedia.i == null)
            {
                Logging.HorusLog.Trace("RTS", "FactoryRecreateDefer", "Deferring virtual factory anchor recreation: Spawner or Encyclopedia not ready.", 5f);
                return;
            }

            foreach (var factory in activeFactories)
            {
                if (factory == null || !factory.isVirtual) continue;
                if (factory.anchorDestroyed) continue;
                if (Time.unscaledTime < factory.nextAnchorRetryTime) continue;

                // If anchorUnit is already set and alive, skip
                if (factory.anchorUnit != null && !IsDeadUnit(factory.anchorUnit)) continue;

                // Resolve visual building name
                var preset = config?.factoryPresets?.FirstOrDefault(p => string.Equals(p.presetName, factory.presetName ?? factory.displayName, StringComparison.OrdinalIgnoreCase));
                string visualBuildingName = preset?.visualBuilding ?? factory.visualBuilding;

                if (string.IsNullOrEmpty(visualBuildingName)) continue;

                int faction = factory.factionId;
                var factions = FactionRegistry.factions;
                if (factions == null || faction < 0 || faction >= factions.Count) continue;

                Faction factionObj = factions[faction];
                FactionHQ hq = FactionRegistry.HQFromFaction(factionObj);

                Vector3 localPos = new GlobalPosition(factory.globalX, factory.globalY, factory.globalZ).ToLocalPosition();
                GlobalPosition globalPos = localPos.ToGlobalPosition();

                Unit spawned = SpawnFactoryVisual(factory, visualBuildingName, globalPos, factory.yaw, hq, "recreate");
                if (spawned != null)
                {
                    factory.anchorUnit = spawned;
                    factory.anchorUnitName = spawned.unitName;
                    factory.anchorDestroyed = false;
                    factory.anchorSpawnFailures = 0;
                    factory.nextAnchorRetryTime = 0f;
                    factory.lastStatus = "Ready";
                    HorusLog.Info("Factory", $"[HORUS RTS] Recreated visual building '{spawned.unitName}' for factory '{factory.displayName}' id={factory.id}");
                }
                else
                {
                    factory.anchorSpawnFailures++;
                    float delay = Mathf.Min(60f, 5f * Mathf.Pow(2f, Mathf.Min(4, factory.anchorSpawnFailures - 1)));
                    factory.nextAnchorRetryTime = Time.unscaledTime + delay;
                    factory.lastStatus = $"Visual spawn failed; retry in {delay:F0}s";
                    LogBlocked(factory, "anchor-retry", $"[HORUS RTS] Factory visual retry delayed {delay:F0}s: {factory.displayName}");
                }
            }
        }

        public UnitDefinition ResolveVisualBuildingDefinition(string requestedName, out string resolvedName, bool logDetails = true)
        {
            resolvedName = requestedName;
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null) return null;

            string searchName = ResolveVisualAlias(requestedName);
            resolvedName = searchName;

            UnitDefinition exact = FindBuildingDefinitionExact(searchName);
            if (exact != null)
            {
                resolvedName = exact.unitName;
                if (logDetails && !string.Equals(requestedName, exact.unitName, StringComparison.OrdinalIgnoreCase))
                {
                    HorusLog.Info("Factory", $"[HORUS RTS] Factory visual resolved: {requestedName} -> {exact.unitName}");
                }
                return exact;
            }

            // Try substring match for loose compatibility with edited configs.
            foreach (var b in Encyclopedia.i.buildings)
            {
                if (b != null && !string.IsNullOrEmpty(searchName) &&
                    (b.unitName.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     searchName.IndexOf(b.unitName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    resolvedName = b.unitName;
                    if (logDetails)
                    {
                        HorusLog.Info("Factory", $"[HORUS RTS] Factory visual resolved: {requestedName} -> {b.unitName}");
                    }
                    return b;
                }
            }

            UnitDefinition fallback = FindBuildingDefinitionExact("Large Factory")
                ?? FindBuildingDefinitionExact("Pillbox")
                ?? Encyclopedia.i.buildings.FirstOrDefault(b => b != null);
            if (fallback != null)
            {
                resolvedName = fallback.unitName;
                if (logDetails)
                {
                    HorusLog.Warning("Factory", $"[HORUS RTS] Factory visual resolved by emergency fallback: {requestedName} -> {fallback.unitName}");
                }
                return fallback;
            }

            if (logDetails)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Factory visual UnitDefinition not found: requested={requestedName}");
            }
            return null;
        }

        private UnitDefinition FindBuildingDefinitionFallback(string name)
        {
            return ResolveVisualBuildingDefinition(name, out _, false);
        }

        private UnitDefinition FindBuildingDefinitionExact(string name)
        {
            if (Encyclopedia.i == null || Encyclopedia.i.buildings == null || string.IsNullOrEmpty(name)) return null;
            foreach (var b in Encyclopedia.i.buildings)
            {
                if (b != null &&
                    (string.Equals(b.unitName, name, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(b.jsonKey, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return b;
                }
            }
            return null;
        }

        private static string ResolveVisualAlias(string name)
        {
            if (string.Equals(name, "Solar Array", StringComparison.OrdinalIgnoreCase)) return "Storage Tank";
            if (string.Equals(name, "Vehicle Factory", StringComparison.OrdinalIgnoreCase)) return "Large Factory";
            if (string.Equals(name, "Hangar", StringComparison.OrdinalIgnoreCase)) return "Medium Aircraft Hangar";
            if (string.Equals(name, "Warehouse", StringComparison.OrdinalIgnoreCase)) return "Large Factory";
            return name;
        }

        private Unit SpawnFactoryVisual(RtsFactory factory, string requestedVisualBuilding, GlobalPosition globalPos, float yaw, FactionHQ hq, string context)
        {
            string resolvedName;
            UnitDefinition buildingDef = ResolveVisualBuildingDefinition(requestedVisualBuilding, out resolvedName, true);
            HorusLog.Info("Factory", $"[HORUS RTS] Factory visual request ({context}): preset={factory.displayName} requested={requestedVisualBuilding} resolved={resolvedName} id={factory.id} faction={factory.factionName} pos=({factory.globalX:F1},{factory.globalY:F1},{factory.globalZ:F1})");

            if (buildingDef == null)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Factory visual UnitDefinition not found: preset={factory.displayName} requested={requestedVisualBuilding} resolved={resolvedName}");
                return null;
            }

            HorusLog.Info("Factory", $"[HORUS RTS] Factory visual UnitDefinition found: {buildingDef.unitName} key={buildingDef.jsonKey}");

            if (Spawner.i == null)
            {
                HorusLog.Error("Factory", "[HORUS RTS] Factory visual spawn failed: Spawner.i is null.");
                return null;
            }

            string uniqueName = (buildingDef.jsonKey ?? "building") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            var request = new HorusSpawnRequest
            {
                Definition = buildingDef,
                Position = globalPos,
                Rotation = rot,
                HQ = hq,
                UniqueName = uniqueName
            };
            string authorizationError = "Horus manager unavailable.";
            if (HorusManager.Instance == null ||
                !HorusManager.Instance.TryAuthorizeSpawnRequest(request, false, out authorizationError))
            {
                HorusLog.Warning("Factory", $"[HORUS RTS] Factory visual blocked: {authorizationError}");
                return null;
            }
            Unit spawned = HorusSpawnService.Spawn(request).Unit;
            if (spawned == null)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Factory visual spawn failed: {buildingDef.unitName} for factory id={factory.id}");
                return null;
            }

            HorusManager.Instance.AddHorusSpawnedUnit(spawned);
            HorusLog.Info("Factory", $"[HORUS RTS] Factory visual building spawned: {buildingDef.unitName} spawnedUnit={spawned.unitName} factoryId={factory.id}");
            return spawned;
        }

        // ─── Unit Spawning Logic ──────────────────────────────────────────────────

        private UnitDefinition ResolveProductionDefinition(RtsFactory factory, string unitKey)
        {
            UnitDefinition def = HorusManager.Instance.FindUnitDefinitionByName(unitKey);
            if (def != null && IsDefinitionCompatibleWithFactory(factory, def))
            {
                return def;
            }

            if (def == null)
            {
                LogBlocked(factory, "production-unit-missing-" + unitKey, $"[HORUS RTS] Factory production blocked: missing production unit '{unitKey}'");
            }
            else
            {
                LogBlocked(factory, "production-unit-incompatible-" + unitKey, $"[HORUS RTS] Factory production blocked: '{def.unitName}' is incompatible with {factory.factoryType}");
            }

            // Never silently substitute a different combat unit. Legacy unitName
            // values are already resolved above; a missing/incompatible key must be
            // corrected explicitly by the user.
            return null;
        }

        private bool IsDefinitionCompatibleWithFactory(RtsFactory factory, UnitDefinition def)
        {
            if (factory == null || def == null) return false;
            // Live ordnance is intentionally individual-only. Never let a fallback,
            // Mixed factory or persisted queue turn missiles into repeating production.
            CatalogEntry catalogEntry = HorusManager.FindCatalogEntry(def);
            if (catalogEntry?.IsLiveOrdnance == true || def is MissileDefinition) return false;
            if (factory.factoryType == RtsFactoryType.MixedProduction) return true;
            if (factory.factoryType == RtsFactoryType.Economy) return false;

            bool isShip = catalogEntry != null
                ? catalogEntry.SpawnKind == SpawnKind.Ship
                : def is ShipDefinition || def.unitPrefab?.GetComponent<Ship>() != null;
            bool isAircraft = catalogEntry != null
                ? catalogEntry.SpawnKind == SpawnKind.Aircraft
                : def is AircraftDefinition || def.unitPrefab?.GetComponent<Aircraft>() != null;
            bool isGround = catalogEntry != null
                ? catalogEntry.SpawnKind == SpawnKind.Vehicle ||
                  catalogEntry.SpawnKind == SpawnKind.Building ||
                  catalogEntry.SpawnKind == SpawnKind.Scenery
                : def is VehicleDefinition || def is BuildingDefinition || def is SceneryDefinition;

            switch (factory.factoryType)
            {
                case RtsFactoryType.GroundProduction:
                    return isGround && !isAircraft && !isShip;
                case RtsFactoryType.AirProduction:
                    return isAircraft && !isShip;
                case RtsFactoryType.NavalProduction:
                    return isShip;
                case RtsFactoryType.DefenseProduction:
                    return isGround && !isAircraft && !isShip;
                default:
                    return false;
            }
        }

        private UnitDefinition FindFirstCompatibleDefinition(RtsFactoryType type)
        {
            if (Encyclopedia.i == null) return null;

            if (type == RtsFactoryType.GroundProduction || type == RtsFactoryType.MixedProduction)
            {
                UnitDefinition vehicle = FirstValidDefinition(Encyclopedia.i.vehicles);
                if (vehicle != null) return vehicle;
            }

            if (type == RtsFactoryType.AirProduction || type == RtsFactoryType.MixedProduction)
            {
                UnitDefinition aircraft = FirstValidDefinition(Encyclopedia.i.aircraft);
                if (aircraft != null) return aircraft;
            }

            if (type == RtsFactoryType.NavalProduction || type == RtsFactoryType.MixedProduction)
            {
                UnitDefinition ship = FirstValidDefinition(Encyclopedia.i.ships);
                if (ship != null) return ship;
            }

            if (type == RtsFactoryType.DefenseProduction || type == RtsFactoryType.MixedProduction)
            {
                UnitDefinition preferredDefense = FirstMatchingDefinition(
                    Encyclopedia.i.buildings,
                    "23mm AAA Emplacement",
                    "IRM-S1 Emplacement",
                    "AT-145 Emplacement",
                    "Guard Tower",
                    "Pillbox",
                    "Radar Station",
                    "Emplacement",
                    "AAA",
                    "AA",
                    "IRM",
                    "SAM",
                    "ATGM",
                    "Tower",
                    "Radar");
                if (preferredDefense != null) return preferredDefense;

                UnitDefinition building = FirstValidDefinition(Encyclopedia.i.buildings);
                if (building != null) return building;
            }

            return null;
        }

        private UnitDefinition FirstValidDefinition(System.Collections.IEnumerable source)
        {
            if (source == null) return null;
            foreach (var item in source)
            {
                if (item is UnitDefinition def)
                {
                    return def;
                }
            }
            return null;
        }

        private UnitDefinition FirstMatchingDefinition(System.Collections.IEnumerable source, params string[] terms)
        {
            if (source == null) return null;
            foreach (string term in terms)
            {
                if (string.IsNullOrEmpty(term)) continue;
                foreach (var item in source)
                {
                    if (!(item is UnitDefinition def)) continue;
                    if ((!string.IsNullOrEmpty(def.unitName) && def.unitName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(def.jsonKey) && def.jsonKey.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return def;
                    }
                }
            }
            return null;
        }

        private Unit SpawnFactoryUnit(RtsFactory factory, UnitDefinition def)
        {
            if (Spawner.i == null)
            {
                HorusLog.Error("Factory", "[HORUS RTS] Factory production failed: Spawner.i is null.");
                return null;
            }

            int faction = factory.factionId;
            var factions = FactionRegistry.factions;
            if (factions == null || faction < 0 || faction >= factions.Count)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Factory production failed: invalid faction index {faction}.");
                return null;
            }

            Faction factionObj = factions[faction];
            FactionHQ hq = FactionRegistry.HQFromFaction(factionObj);

            Vector3 factoryLocalPos = new GlobalPosition(factory.globalX, factory.globalY, factory.globalZ).ToLocalPosition();
            float spawnYaw = GetFactorySpawnYaw(factory);
            Quaternion spawnRot = Quaternion.Euler(0f, spawnYaw, 0f);
            Vector3 forward = Quaternion.Euler(0f, factory.yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, factory.yaw, 0f) * Vector3.right;

            CatalogEntry catalogEntry = HorusManager.FindCatalogEntry(def);
            bool isShip = catalogEntry != null
                ? catalogEntry.SpawnKind == SpawnKind.Ship
                : def is ShipDefinition || def.unitPrefab?.GetComponent<Ship>() != null;
            bool isAircraft = catalogEntry != null
                ? catalogEntry.SpawnKind == SpawnKind.Aircraft
                : def is AircraftDefinition || def.unitPrefab?.GetComponent<Aircraft>() != null;
            Unit spawned = null;

            if (isShip)
            {
                float spacing = Mathf.Max(80f, factory.spawnRadius);
                Vector3 spawnLocalPos = factoryLocalPos + forward * spacing + right * UnityEngine.Random.Range(-spacing * 0.35f, spacing * 0.35f);
                float targetY = Datum.LocalSeaY + def.spawnOffset.y + HorusPlugin.ShipSpawnLift.Value;
                Vector3 finalPos = new Vector3(spawnLocalPos.x, targetY, spawnLocalPos.z);
                GlobalPosition globalPos = finalPos.ToGlobalPosition();
                LogFactorySpawnPoint(factory, def, finalPos, "naval-sea", spawnYaw);
                spawned = HorusManager.Instance.SpawnShipSafe(def, globalPos, spawnYaw, faction);
            }
            else if (isAircraft)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Max(10f, factory.spawnRadius);
                float baseY = GetFactoryGroundHeight(factoryLocalPos, factoryLocalPos.y);
                Vector3 airPos = new Vector3(
                    factoryLocalPos.x + offset.x,
                    baseY + 1000f + def.spawnOffset.y,
                    factoryLocalPos.z + offset.y);
                GlobalPosition globalPos = airPos.ToGlobalPosition();
                string uniqueName = (def.jsonKey ?? "aircraft") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                LogFactorySpawnPoint(factory, def, airPos, "airborne", spawnYaw);
                Aircraft prefabAircraft = def.unitPrefab != null ? def.unitPrefab.GetComponent<Aircraft>() : null;
                AircraftDefinition effectiveAircraftDefinition = def as AircraftDefinition ??
                    prefabAircraft?.definition as AircraftDefinition;
                float defaultFuel = effectiveAircraftDefinition?.aircraftParameters != null
                    ? Mathf.Clamp01(effectiveAircraftDefinition.aircraftParameters.DefaultFuelLevel)
                    : 1f;
                var request = new HorusSpawnRequest
                {
                    Definition = def,
                    Position = globalPos,
                    Rotation = spawnRot,
                    HQ = hq,
                    UniqueName = uniqueName,
                    Aircraft = new AircraftSpawnOptions
                    {
                        // The authoritative spawn service resolves and validates a
                        // fresh default (or a dimensioned empty fallback) before the
                        // native network spawn.
                        Loadout = null,
                        FuelRatio = defaultFuel,
                        Skill = 1f,
                        Bravery = 0.5f
                    }
                };
                if (!HorusManager.Instance.TryAuthorizeSpawnRequest(request, false, out string authorizationError))
                {
                    factory.lastStatus = "Production blocked: " + authorizationError;
                    return null;
                }
                HorusSpawnResult result = HorusSpawnService.Spawn(request);
                spawned = result.Unit;
                if (!result.Success) factory.lastStatus = "Production failed: " + result.Message;
                if (spawned != null) HorusManager.Instance.AddHorusSpawnedUnit(spawned);
            }
            else
            {
                Vector3 groundCandidate = GetFactoryGroundSpawnPosition(factory, def, factoryLocalPos, forward, right);
                Vector3 finalPos = groundCandidate;
                if (catalogEntry?.PlacementSurface == PlacementSurface.Sea)
                    finalPos.y = Datum.LocalSeaY + def.spawnOffset.y;
                GlobalPosition globalPos = finalPos.ToGlobalPosition();
                string uniqueName = (def.jsonKey ?? "unit") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                LogFactorySpawnPoint(factory, def, finalPos, "front-ground", spawnYaw);
                var request = new HorusSpawnRequest
                {
                    Definition = def,
                    Position = globalPos,
                    Rotation = spawnRot,
                    HQ = hq,
                    UniqueName = uniqueName,
                    Stationary = factory.factoryType == RtsFactoryType.DefenseProduction
                };
                if (!HorusManager.Instance.TryAuthorizeSpawnRequest(request, false, out string authorizationError))
                {
                    factory.lastStatus = "Production blocked: " + authorizationError;
                    return null;
                }
                HorusSpawnResult result = HorusSpawnService.Spawn(request);
                spawned = result.Unit;
                if (!result.Success) factory.lastStatus = "Production failed: " + result.Message;
                if (spawned != null)
                {
                    if (catalogEntry == null || catalogEntry.PlacementSurface == PlacementSurface.Ground)
                        CorrectFactoryGroundSpawn(spawned, finalPos, spawnRot);
                    HorusManager.Instance.AddHorusSpawnedUnit(spawned);
                    if (factory.factoryType == RtsFactoryType.DefenseProduction)
                    {
                        if (spawned is GroundVehicle vehicle)
                        {
                            vehicle.SetHoldPosition(true);
                        }
                    }
                }
            }

            if (spawned != null && factory.useRallyPoint)
            {
                FaceUnitTowardRallyPoint(spawned, factory);
                GlobalPosition rally = new GlobalPosition(factory.rallyX, factory.rallyY, factory.rallyZ);
                if (HorusOrders.TrySetDestination(spawned, rally, playerCommand: false, out string rallyReason))
                    HorusLog.Info("Factory", $"[HORUS RTS] Rally order issued to {spawned.unitName}.");
                else
                    HorusLog.Warning("Factory", $"[HORUS RTS] Rally order skipped for {spawned.unitName}: {rallyReason}.");
            }

            return spawned;
        }

        private Vector3 GetFactoryGroundSpawnPosition(RtsFactory factory, UnitDefinition def, Vector3 factoryLocalPos, Vector3 forward, Vector3 right)
        {
            int category = GetFactoryProductionCategory(def);
            bool staticDefense = factory.factoryType == RtsFactoryType.DefenseProduction || category == 3 || category == 4;
            float baseDistance = staticDefense ? 45f : 65f;
            float laneSpacing = staticDefense ? 14f : 18f;
            float[] distanceSteps = { 0f, 18f, 36f, 60f, 90f };
            float[] laneSteps = { 0f, -1f, 1f, -2f, 2f };

            foreach (float distanceStep in distanceSteps)
            {
                foreach (float laneStep in laneSteps)
                {
                    Vector3 candidate = factoryLocalPos + forward * (baseDistance + distanceStep) + right * (laneStep * laneSpacing);
                    if (TryBuildValidatedGroundSpawn(factory, def, category, candidate, out Vector3 finalPos))
                    {
                        return finalPos;
                    }
                }
            }

            float sideSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            foreach (float distanceStep in distanceSteps)
            {
                Vector3 sideCandidate = factoryLocalPos + right * sideSign * (baseDistance + distanceStep) + forward * laneSpacing;
                if (TryBuildValidatedGroundSpawn(factory, def, category, sideCandidate, out Vector3 finalPos))
                {
                    HorusLog.Warning("Factory", $"[HORUS RTS] Factory front spawn blocked; using side fallback for {def.unitName} from {factory.displayName}.");
                    return finalPos;
                }
            }

            Vector3 emergency = factoryLocalPos + forward * (baseDistance + 120f);
            if (TryBuildGroundSpawnWithoutAnchorClearance(def, category, emergency, out Vector3 emergencyPos))
            {
                HorusLog.Warning("Factory", $"[HORUS RTS] Factory ground spawn used emergency terrain-only fallback for {def.unitName} from {factory.displayName}.");
                return emergencyPos;
            }

            emergency.y = -100f + (def != null ? def.spawnOffset.y : 0f);
            if (category == 1) emergency.y += 2f;
            HorusLog.Warning("Factory", $"[HORUS RTS] Factory ground spawn could not find terrain; using low emergency fallback for {def.unitName} from {factory.displayName}.");
            return emergency;
        }

        private float GetFactoryGroundHeight(Vector3 localPos, float fallbackY)
        {
            return TrySampleFactoryTerrainHeight(localPos, out float groundY) ? groundY : fallbackY;
        }

        private float GetFactoryGroundHeight(Vector3 localPos, Vector3 fallbackLocalPos, float fallbackY)
        {
            if (TrySampleFactoryTerrainHeight(localPos, out float groundY))
            {
                return groundY;
            }
            if (TrySampleFactoryTerrainHeight(fallbackLocalPos, out float fallbackGroundY))
            {
                return fallbackGroundY;
            }
            return fallbackY;
        }

        private bool TryBuildValidatedGroundSpawn(RtsFactory factory, UnitDefinition def, int category, Vector3 candidate, out Vector3 finalPos)
        {
            finalPos = Vector3.zero;
            if (!TryBuildGroundSpawnWithoutAnchorClearance(def, category, candidate, out finalPos))
            {
                return false;
            }

            float centerClearance = category == 1 ? 55f : 38f;
            float footprintMargin = category == 1 ? 10f : 7f;
            if (IsInsideFactoryAnchorFootprint(factory, finalPos, footprintMargin))
            {
                return false;
            }

            Vector2 factoryXZ = new Vector2(new GlobalPosition(factory.globalX, factory.globalY, factory.globalZ).ToLocalPosition().x, new GlobalPosition(factory.globalX, factory.globalY, factory.globalZ).ToLocalPosition().z);
            Vector2 spawnXZ = new Vector2(finalPos.x, finalPos.z);
            if (Vector2.Distance(factoryXZ, spawnXZ) < centerClearance)
            {
                return false;
            }

            return true;
        }

        private bool TryBuildGroundSpawnWithoutAnchorClearance(UnitDefinition def, int category, Vector3 candidate, out Vector3 finalPos)
        {
            finalPos = candidate;
            if (!TrySampleFactoryTerrainHeight(candidate, out float groundY))
            {
                return false;
            }

            finalPos.y = groundY;
            if (def != null)
            {
                finalPos.y += def.spawnOffset.y;
            }
            if (category == 1)
            {
                finalPos.y += 2f;
            }
            return true;
        }

        private bool TrySampleFactoryTerrainHeight(Vector3 localPos, out float groundY)
        {
            groundY = 0f;
            Vector3 origin = new Vector3(localPos.x, 10000f, localPos.z);
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, FactoryGroundHitCache, 20000f, TerrainLayerMask);
            if (hitCount <= 0) return false;

            bool found = false;
            float highest = float.MinValue;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = FactoryGroundHitCache[i].collider;
                if (hitCollider == null) continue;
                if (hitCollider.GetComponentInParent<Unit>() != null) continue;

                float y = FactoryGroundHitCache[i].point.y;
                if (!found || y > highest)
                {
                    highest = y;
                    found = true;
                }
            }

            if (!found) return false;
            groundY = highest;
            return true;
        }

        private bool IsInsideFactoryAnchorFootprint(RtsFactory factory, Vector3 localPos, float margin)
        {
            Unit anchor = factory?.anchorUnit;
            if (anchor == null) return false;

            Collider[] colliders = anchor.GetComponentsInChildren<Collider>();
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                if (localPos.x >= bounds.min.x - margin &&
                    localPos.x <= bounds.max.x + margin &&
                    localPos.z >= bounds.min.z - margin &&
                    localPos.z <= bounds.max.z + margin)
                {
                    return true;
                }
            }
            return false;
        }

        private void CorrectFactoryGroundSpawn(Unit spawned, Vector3 finalPos, Quaternion spawnRot)
        {
            if (spawned == null) return;
            spawned.transform.SetPositionAndRotation(finalPos, spawnRot);

            Rigidbody rb = spawned.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.MovePosition(finalPos);
                rb.MoveRotation(spawnRot);
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private int GetFactoryProductionCategory(UnitDefinition def)
        {
            if (def == null) return 1;
            if (def is AircraftDefinition) return 0;
            if ((HorusManager.Instance != null && HorusManager.Instance.IsShipDefinition(def)) || def is ShipDefinition) return 2;
            if (def is BuildingDefinition) return 3;
            if (def is SceneryDefinition) return 4;
            if (def is VehicleDefinition) return 1;
            return 1;
        }

        private void LogFactorySpawnPoint(RtsFactory factory, UnitDefinition def, Vector3 localPos, string mode, float yaw)
        {
            HorusLog.Info("Factory",
                $"[HORUS RTS] Factory production spawn point: factory={factory.displayName} type={factory.factoryType} unit={def.unitName} mode={mode} local=({localPos.x:F1},{localPos.y:F1},{localPos.z:F1}) yaw={yaw:F0}");
        }

        private float GetFactorySpawnYaw(RtsFactory factory)
        {
            if (factory == null || !factory.useRallyPoint) return factory?.yaw ?? 0f;
            Vector3 origin = new GlobalPosition(factory.globalX, factory.globalY, factory.globalZ).ToLocalPosition();
            Vector3 rally = new GlobalPosition(factory.rallyX, factory.rallyY, factory.rallyZ).ToLocalPosition();
            Vector3 dir = rally - origin;
            dir.y = 0f;
            if (dir.sqrMagnitude <= 0.001f) return factory.yaw;
            return Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles.y;
        }

        private void FaceUnitTowardRallyPoint(Unit spawned, RtsFactory factory)
        {
            Vector3 rallyLocal = new GlobalPosition(factory.rallyX, factory.rallyY, factory.rallyZ).ToLocalPosition();
            Vector3 dir = rallyLocal - spawned.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude <= 0.001f) return;

            spawned.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Rigidbody rb = spawned.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.MoveRotation(spawned.transform.rotation);
            }
        }

        // ─── Actions / Factory Creation ──────────────────────────────────────────

        private RtsFactory CreateRuntimeFactory(FactoryPreset preset, RtsFactoryType type, int factionIndex, GlobalPosition globalPos, float yaw)
        {
            if (preset == null || !CanUseFactoryFaction(factionIndex, out _)) return null;
            var queue = preset.productionUnitKeys != null
                ? new List<string>(preset.productionUnitKeys)
                : new List<string>();

            if (preset.produceUnits && queue.Count == 0)
            {
                queue.AddRange(GetDefaultProductionQueue(type));
            }

            return new RtsFactory
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                displayName = preset.presetName,
                presetName = preset.presetName,
                factionId = factionIndex,
                factionName = FactionRegistry.factions[factionIndex]?.factionName ?? $"Faction{factionIndex}",
                factoryType = type,
                globalX = globalPos.x,
                globalY = globalPos.y,
                globalZ = globalPos.z,
                yaw = yaw,
                enabled = config?.settings?.autoStartFactories ?? true,
                generateIncome = preset.incomePerMinute > 0,
                incomePerMinute = preset.incomePerMinute,
                produceUnits = preset.produceUnits,
                consumeBudgetForProduction = config?.settings?.productionConsumesBudget ?? true,
                productionUnitKeys = queue,
                currentProductionIndex = 0,
                productionIntervalSeconds = preset.productionIntervalSeconds,
                productionTimer = 0f,
                maxActiveProducedUnits = preset.maxActiveProducedUnits,
                spawnRadius = 50f,
                visualBuilding = preset.visualBuilding
            };
        }

        private List<string> GetDefaultProductionQueue(RtsFactoryType type)
        {
            switch (type)
            {
                case RtsFactoryType.GroundProduction:
                    return new List<string> { "Nailer" };
                case RtsFactoryType.AirProduction:
                    return new List<string> { "Cricket" };
                case RtsFactoryType.NavalProduction:
                    return new List<string> { "Goldfinch" };
                case RtsFactoryType.DefenseProduction:
                    return new List<string> { "23mm AAA Emplacement", "IRM-S1 Emplacement", "AT-145 Emplacement", "Guard Tower", "Pillbox", "Radar Station" };
                case RtsFactoryType.MixedProduction:
                    return new List<string> { "Nailer", "Cricket", "Goldfinch", "23mm AAA Emplacement", "IRM-S1 Emplacement", "Guard Tower" };
                default:
                    return new List<string>();
            }
        }

        private bool CanMutateFactories(string action)
        {
            if (HorusPermissions.CanSpawn()) return true;
            LogPermissionBlocked(action);
            return false;
        }

        private void LogPermissionBlocked(string action)
        {
            HorusLog.Warning("Factory", $"[HORUS RTS] Permission blocked: {action}. Host only.");
            if (SceneSingleton<GameplayUI>.i != null)
            {
                SceneSingleton<GameplayUI>.i.GameMessage("Horus: Host only.");
            }
        }

        public bool CanUseFactoryFaction(int factionIndex, out string reason)
        {
            var factions = FactionRegistry.factions;
            if (factions == null || factions.Count == 0)
            {
                reason = "Playable factions are not loaded yet";
                return false;
            }
            if (factionIndex < 0 || factionIndex >= factions.Count)
            {
                reason = "Factories cannot use Neutral; select a playable faction";
                return false;
            }
            Faction faction = factions[factionIndex];
            if (faction == null)
            {
                reason = $"Faction {factionIndex} is unavailable";
                return false;
            }
            if (FactionRegistry.HQFromFaction(faction) == null)
            {
                reason = $"Faction '{faction.factionName}' has no active HQ";
                return false;
            }
            reason = null;
            return true;
        }

        private void ReportFactoryRejected(string reason)
        {
            HorusLog.Warning("Factory", $"[HORUS RTS] Factory action rejected: {reason}.");
            HorusToasts.Show(reason);
            if (SceneSingleton<GameplayUI>.i != null)
                SceneSingleton<GameplayUI>.i.GameMessage("Horus: " + reason);
        }

        public RtsFactory CreateFactoryAtPlacement(Vector3 localPos, float yaw, string presetName, int factionIndex)
        {
            if (!CanMutateFactories("create factory")) return null;
            if (config?.settings == null || !config.settings.enableFactories)
            {
                ReportFactoryRejected("Factory system is disabled in config");
                return null;
            }
            if (!CanUseFactoryFaction(factionIndex, out string factionReason))
            {
                ReportFactoryRejected(factionReason);
                return null;
            }

            var preset = config?.factoryPresets?.FirstOrDefault(p => string.Equals(p.presetName, presetName, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Failed to find preset: {presetName}");
                return null;
            }

            if (activeFactories.Count(f => f.factionId == factionIndex) >= config.settings.maxFactoriesPerFaction)
            {
                HorusLog.Warning("Factory", $"[HORUS RTS] Maximum factory limit reached for faction index {factionIndex}");
                return null;
            }

            var globalPos = localPos.ToGlobalPosition();
            RtsFactoryType type = RtsFactoryType.Economy;
            Enum.TryParse(preset.type, out type);

            var factory = CreateRuntimeFactory(preset, type, factionIndex, globalPos, yaw);
            if (factory == null)
            {
                ReportFactoryRejected("Factory could not be initialized");
                return null;
            }
            factory.isVirtual = true;

            int faction = factionIndex;
            var factions = FactionRegistry.factions;
            if (factions != null && faction >= 0 && faction < factions.Count)
            {
                Faction factionObj = factions[faction];
                FactionHQ hq = FactionRegistry.HQFromFaction(factionObj);

                string visualBuildingName = factory.visualBuilding;
                if (!string.IsNullOrEmpty(visualBuildingName))
                {
                    Unit spawned = SpawnFactoryVisual(factory, visualBuildingName, globalPos, yaw, hq, "create");
                    if (spawned == null)
                    {
                        ReportFactoryRejected("Factory visual could not be spawned; the factory was not registered");
                        return null;
                    }
                    else
                    {
                        factory.anchorUnit = spawned;
                        factory.anchorUnitName = spawned.unitName;
                    }
                }
            }

            activeFactories.Add(factory);
            HorusLog.Info("Factory", $"[HORUS RTS] Factory created: {factory.displayName} / {factory.factionName} / id={factory.id} type={factory.factoryType} pos={localPos}");
            HorusLog.Info("Factory", $"[HORUS RTS] Factory registered: activeFactories={activeFactories.Count} id={factory.id}");
            SaveInstancesInternal();
            return factory;
        }

        public RtsFactory CreateFactoryFromUnit(Unit targetUnit, string presetName)
        {
            if (!CanMutateFactories("create factory from aimed unit")) return null;
            if (targetUnit == null) return null;
            if (config?.settings == null || !config.settings.enableFactories)
            {
                ReportFactoryRejected("Factory system is disabled in config");
                return null;
            }

            var preset = config?.factoryPresets?.FirstOrDefault(p => string.Equals(p.presetName, presetName, StringComparison.OrdinalIgnoreCase));
            if (preset == null) return null;

            // Determine faction index from targetUnit
            int factionIndex = -1;
            var factions = FactionRegistry.factions;
            if (factions != null)
            {
                var friendlyHQ = targetUnit.NetworkHQ ?? targetUnit.MapHQ ?? targetUnit.Editor_HQ;
                for (int i = 0; i < factions.Count; i++)
                {
                    if (FactionRegistry.HQFromFaction(factions[i]) == friendlyHQ)
                    {
                        factionIndex = i;
                        break;
                    }
                }
            }
            if (!CanUseFactoryFaction(factionIndex, out string factionReason))
            {
                ReportFactoryRejected(factionReason);
                return null;
            }

            if (activeFactories.Count(f => f.factionId == factionIndex) >= config.settings.maxFactoriesPerFaction)
            {
                HorusLog.Warning("Factory", $"[HORUS RTS] Maximum factory limit reached for faction index {factionIndex}");
                return null;
            }

            var localPos = targetUnit.transform.position;
            var globalPos = localPos.ToGlobalPosition();
            float yaw = targetUnit.transform.eulerAngles.y;

            RtsFactoryType type = RtsFactoryType.Economy;
            Enum.TryParse(preset.type, out type);

            var factory = CreateRuntimeFactory(preset, type, factionIndex, globalPos, yaw);
            if (factory == null)
            {
                ReportFactoryRejected("Factory could not be initialized");
                return null;
            }
            factory.anchorUnit = targetUnit;
            factory.anchorUnitName = targetUnit.unitName;
            factory.isVirtual = false;

            activeFactories.Add(factory);
            HorusLog.Info("Factory", $"[HORUS RTS] Factory created from aimed unit: target={targetUnit.unitName} preset={factory.displayName} faction={factory.factionName} id={factory.id} pos={localPos}");
            HorusLog.Info("Factory", $"[HORUS RTS] Factory registered: activeFactories={activeFactories.Count} id={factory.id}");
            SaveInstancesInternal();
            return factory;
        }

        public void DeleteFactory(RtsFactory factory)
        {
            if (!CanMutateFactories("delete factory")) return;
            if (factory == null) return;

            // Clean up visual building if it is virtual
            if (factory.isVirtual && factory.anchorUnit != null)
            {
                try
                {
                    if (HorusPermissions.IsMultiplayer())
                    {
                        NetworkServer.Destroy(factory.anchorUnit.gameObject);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(factory.anchorUnit.gameObject);
                    }
                    HorusLog.Info("Factory", $"[HORUS RTS] Destroyed visual building '{factory.anchorUnit.unitName}' associated with deleted factory.");
                }
                catch (Exception ex)
                {
                    HorusLog.Error("Factory", $"[HORUS RTS] Failed to destroy visual building: {ex.Message}");
                }
                factory.anchorUnit = null;
            }

            activeFactories.Remove(factory);
            HorusLog.Info("Factory", $"[HORUS RTS] Factory deleted: {factory.displayName} id={factory.id}");
            SaveInstancesInternal();
        }

        // ─── Auto-detect Airbase/Carriers ──────────────────────────────────────────

        public void SetFactoryEnabled(RtsFactory factory, bool enabled)
        {
            if (!CanMutateFactories(enabled ? "enable factory" : "disable factory")) return;
            if (factory == null) return;
            factory.enabled = enabled && !factory.anchorDestroyed;
            HorusLog.Info("Factory", $"[HORUS RTS] Factory {(factory.enabled ? "enabled" : "disabled")}: {factory.displayName} id={factory.id}");
            SaveInstancesInternal();
        }

        public void SetFactoryProductionEnabled(RtsFactory factory, bool enabled)
        {
            if (!CanMutateFactories(enabled ? "start factory production" : "stop factory production")) return;
            if (factory == null) return;
            factory.produceUnits = enabled;
            HorusLog.Info("Factory", $"[HORUS RTS] Factory production {(enabled ? "enabled" : "disabled")}: {factory.displayName} id={factory.id}");
            SaveInstancesInternal();
        }

        public void SetFactoryConsumesBudget(RtsFactory factory, bool consumesBudget)
        {
            if (!CanMutateFactories("edit factory production budget mode")) return;
            if (factory == null) return;
            factory.consumeBudgetForProduction = consumesBudget;
            SaveInstancesInternal();
        }

        public void StartAllFactories()
        {
            if (!CanMutateFactories("start all factories")) return;
            foreach (var factory in activeFactories)
            {
                if (factory != null && !factory.anchorDestroyed) factory.enabled = true;
            }
            HorusLog.Info("Factory", "[HORUS RTS] All factories started.");
            SaveInstancesInternal();
        }

        public void StopAllFactories()
        {
            if (!CanMutateFactories("stop all factories")) return;
            foreach (var factory in activeFactories)
            {
                if (factory != null) factory.enabled = false;
            }
            HorusLog.Info("Factory", "[HORUS RTS] All factories stopped.");
            SaveInstancesInternal();
        }

        public void AddUnitToProductionQueue(RtsFactory factory, UnitDefinition unitDefinition)
        {
            if (!CanMutateFactories("add unit to production queue")) return;
            CatalogEntry entry = HorusManager.FindCatalogEntry(unitDefinition);
            if (entry?.IsLiveOrdnance == true || unitDefinition is MissileDefinition)
            {
                HorusToasts.Show("Live ordnance cannot be added to factory queues");
                HorusLog.Warning("Factory", "Blocked live ordnance from a production queue.");
                return;
            }
            if (factory == null || unitDefinition == null) return;
            if (!IsDefinitionCompatibleWithFactory(factory, unitDefinition))
            {
                HorusToasts.Show($"{unitDefinition.unitName} is incompatible with {factory.factoryType}");
                return;
            }
            var probe = new HorusSpawnRequest { Definition = unitDefinition };
            string authorizationError = "Horus manager unavailable.";
            if (HorusManager.Instance == null ||
                !HorusManager.Instance.TryAuthorizeSpawnRequest(probe, false, out authorizationError))
            {
                HorusToasts.Show("Factory queue blocked: " + authorizationError);
                return;
            }
            if (factory.productionUnitKeys == null) factory.productionUnitKeys = new List<string>();
            string stableKey = entry?.Key;
            if (string.IsNullOrWhiteSpace(stableKey)) stableKey = unitDefinition.jsonKey;
            if (string.IsNullOrWhiteSpace(stableKey)) stableKey = unitDefinition.unitName;
            factory.productionUnitKeys.Add(stableKey);
            HorusLog.Info("Factory", $"[HORUS RTS] Factory queue add: {factory.displayName} + {unitDefinition.unitName}");
            SaveInstancesInternal();
        }

        public void RemoveProductionQueueItem(RtsFactory factory, int index)
        {
            if (!CanMutateFactories("remove production queue item")) return;
            if (factory == null || factory.productionUnitKeys == null || index < 0 || index >= factory.productionUnitKeys.Count) return;
            string removed = factory.productionUnitKeys[index];
            factory.productionUnitKeys.RemoveAt(index);
            if (factory.productionUnitKeys.Count == 0) factory.currentProductionIndex = 0;
            else if (factory.currentProductionIndex >= factory.productionUnitKeys.Count) factory.currentProductionIndex = 0;
            HorusLog.Info("Factory", $"[HORUS RTS] Factory queue remove: {factory.displayName} - {removed}");
            SaveInstancesInternal();
        }

        public void ClearProductionQueue(RtsFactory factory)
        {
            if (!CanMutateFactories("clear production queue")) return;
            if (factory == null) return;
            if (factory.productionUnitKeys == null) factory.productionUnitKeys = new List<string>();
            factory.productionUnitKeys.Clear();
            factory.currentProductionIndex = 0;
            factory.productionTimer = 0f;
            HorusLog.Info("Factory", $"[HORUS RTS] Factory queue cleared: {factory.displayName}");
            SaveInstancesInternal();
        }

        public void SetRallyPoint(RtsFactory factory, Vector3 localRallyPoint)
        {
            if (!CanMutateFactories("set rally point")) return;
            if (factory == null) return;
            var globalRally = localRallyPoint.ToGlobalPosition();
            factory.useRallyPoint = true;
            factory.rallyX = globalRally.x;
            factory.rallyY = globalRally.y;
            factory.rallyZ = globalRally.z;
            HorusLog.Info("Factory", $"[HORUS RTS] Factory rally point set: {factory.displayName} ({factory.rallyX:F1},{factory.rallyY:F1},{factory.rallyZ:F1})");
            SaveInstancesInternal();
        }

        public void ClearRallyPoint(RtsFactory factory)
        {
            if (!CanMutateFactories("clear rally point")) return;
            if (factory == null) return;
            factory.useRallyPoint = false;
            HorusLog.Info("Factory", $"[HORUS RTS] Factory rally point cleared: {factory.displayName}");
            SaveInstancesInternal();
        }

        public string GetAnchorStatus(RtsFactory factory)
        {
            if (factory == null) return "missing";
            if (factory.anchorDestroyed) return "destroyed";
            if (factory.anchorUnit != null && IsDeadUnit(factory.anchorUnit)) return "destroyed";
            if (factory.anchorUnit != null) return factory.isVirtual ? "virtual" : "attached";
            if (factory.isVirtual) return "virtual / missing visual";
            return string.IsNullOrEmpty(factory.anchorUnitName) ? "virtual" : "attached / missing";
        }

        public void AutoDetectFactories()
        {
            if (!HorusPermissions.CanSpawn())
            {
                LogPermissionBlocked("auto-detect factories");
                return;
            }
            if (UnitRegistry.allUnits == null || config == null) return;

            foreach (var unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.gameObject == null || unit.disabled || unit.unitState == Unit.UnitState.Destroyed) continue;

                bool isAirbase = unit is Building && (unit.GetComponent<Airbase>() != null || unit.GetComponentInChildren<Airbase>(true) != null);
                bool isCarrier = unit is Ship && (unit.GetComponent<Airbase>() != null || unit.GetComponentInChildren<Airbase>(true) != null);

                if (isAirbase && config.settings.autoCreateFactoryForAirbaseUnits)
                {
                    if (!activeFactories.Any(f => f.anchorUnit == unit))
                    {
                        CreateFactoryFromUnit(unit, "Airbase Production");
                    }
                }
                else if (isCarrier && config.settings.autoCreateFactoryForCarriers)
                {
                    if (!activeFactories.Any(f => f.anchorUnit == unit))
                    {
                        CreateFactoryFromUnit(unit, "Naval Yard");
                    }
                }
            }
        }

        // ─── Persistent JSON Serialization ──────────────────────────────────────

        public void ReloadConfig()
        {
            if (!CanMutateFactories("reload factory config")) return;
            LoadOrCreateConfig();
            HorusLog.Info("Factory", "[HORUS RTS] Factory configuration reloaded.");
        }

        public void LoadOrCreateConfig()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);

                if (!File.Exists(ConfigPath))
                {
                    config = new RtsFactoriesConfig
                    {
                        settings = new RtsFactoriesSettings(),
                        factoryPresets = GetDefaultPresets()
                    };
                    SaveConfig(ConfigPath, config);
                    HorusLog.Info("Factory", $"[HORUS RTS] Created default factory config at {ConfigPath}");
                }
                else
                {
                    config = LoadConfig(ConfigPath);
                }
                bool migrated = NormalizeConfig(config);
                if (migrated)
                {
                    SaveConfig(ConfigPath, config);
                    HorusLog.Info("Factory", "[HORUS RTS] Migrated factory config to the current schema.");
                }
            }
            catch (Exception ex)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Config load failed: {ex.Message}. Using defaults.");
                config = new RtsFactoriesConfig
                {
                    settings = new RtsFactoriesSettings(),
                    factoryPresets = GetDefaultPresets()
                };
                NormalizeConfig(config);
                SaveConfig(ConfigPath, config);
            }
        }

        private bool NormalizeConfig(RtsFactoriesConfig cfg)
        {
            if (cfg == null) return false;
            bool changed = false;
            if (cfg.version < 1) { cfg.version = 1; changed = true; }
            if (cfg.settings == null) { cfg.settings = new RtsFactoriesSettings(); changed = true; }
            if (cfg.settings.maxFactoriesPerFaction <= 0)
            {
                cfg.settings.maxFactoriesPerFaction = 10;
                changed = true;
            }
            if (cfg.factoryPresets == null) { cfg.factoryPresets = new List<FactoryPreset>(); changed = true; }

            var defaults = GetDefaultPresets();
            foreach (var defaultPreset in defaults)
            {
                var existing = cfg.factoryPresets.FirstOrDefault(p => string.Equals(p.presetName, defaultPreset.presetName, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    cfg.factoryPresets.Add(ClonePreset(defaultPreset));
                    changed = true;
                    HorusLog.Info("Factory", $"[HORUS RTS] Factory preset added from defaults: {defaultPreset.presetName} visual={defaultPreset.visualBuilding}");
                    continue;
                }

                if (string.IsNullOrEmpty(existing.type)) { existing.type = defaultPreset.type; changed = true; }
                if (string.IsNullOrEmpty(existing.visualBuilding)) { existing.visualBuilding = defaultPreset.visualBuilding; changed = true; }
                if (existing.productionIntervalSeconds <= 0f) { existing.productionIntervalSeconds = defaultPreset.productionIntervalSeconds; changed = true; }
                if (existing.maxActiveProducedUnits < 0 || (existing.produceUnits && existing.maxActiveProducedUnits == 0))
                {
                    existing.maxActiveProducedUnits = defaultPreset.maxActiveProducedUnits;
                    changed = true;
                }
                if (existing.productionUnitKeys == null) { existing.productionUnitKeys = new List<string>(); changed = true; }
                if (existing.produceUnits && existing.productionUnitKeys.Count == 0 && defaultPreset.productionUnitKeys != null)
                {
                    existing.productionUnitKeys = new List<string>(defaultPreset.productionUnitKeys);
                    changed = true;
                }
            }

            foreach (var preset in cfg.factoryPresets)
            {
                if (preset.productionUnitKeys == null) { preset.productionUnitKeys = new List<string>(); changed = true; }
                HorusLog.Info("Factory", $"[HORUS RTS] Factory preset loaded: {preset.presetName} visual={preset.visualBuilding}");
            }
            return changed;
        }

        private static FactoryPreset ClonePreset(FactoryPreset preset)
        {
            return new FactoryPreset
            {
                presetName = preset.presetName,
                type = preset.type,
                incomePerMinute = preset.incomePerMinute,
                produceUnits = preset.produceUnits,
                productionIntervalSeconds = preset.productionIntervalSeconds,
                maxActiveProducedUnits = preset.maxActiveProducedUnits,
                productionUnitKeys = preset.productionUnitKeys != null ? new List<string>(preset.productionUnitKeys) : new List<string>(),
                visualBuilding = preset.visualBuilding
            };
        }

        public void ResetPresetsToDefaults()
        {
            if (!CanMutateFactories("reset factory presets to defaults")) return;
            config = new RtsFactoriesConfig
            {
                settings = new RtsFactoriesSettings(),
                factoryPresets = GetDefaultPresets()
            };
            NormalizeConfig(config);
            SaveConfig(ConfigPath, config);
            HorusLog.Info("Factory", "[HORUS RTS] Factory presets reset to defaults.");
        }

        private static RtsFactoriesConfig LoadConfig(string path)
        {
            var cfg = new RtsFactoriesConfig
            {
                settings = new RtsFactoriesSettings(),
                factoryPresets = GetDefaultPresets()
            };

            if (!File.Exists(path)) return cfg;

            try
            {
                string text = File.ReadAllText(path);
                
                // Parse settings block
                var settingsMatch = Regex.Match(text, @"""settings""\s*:\s*\{([^}]+)\}");
                if (settingsMatch.Success)
                {
                    string settingsJson = "{" + settingsMatch.Groups[1].Value + "}";
                    var loadedSettings = JsonUtility.FromJson<RtsFactoriesSettings>(settingsJson);
                    if (loadedSettings != null) cfg.settings = loadedSettings;
                }

                // Parse factoryPresets block
                var presetsMatch = Regex.Match(text, @"""factoryPresets""\s*:\s*\{([\s\S]+)\}\s*\}");
                if (presetsMatch.Success)
                {
                    string presetsContent = presetsMatch.Groups[1].Value;
                    var presetMatches = Regex.Matches(presetsContent, @"""([^""]+)""\s*:\s*\{([^}]+)\}");
                    if (presetMatches.Count > 0)
                    {
                        cfg.factoryPresets.Clear();
                        foreach (Match m in presetMatches)
                        {
                            string presetName = m.Groups[1].Value;
                            string body = m.Groups[2].Value;
                            string presetJson = "{" + body + "}";
                            var preset = JsonUtility.FromJson<FactoryPreset>(presetJson);
                            if (preset != null)
                            {
                                preset.presetName = presetName;
                                cfg.factoryPresets.Add(preset);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HorusLog.Warning("Factory", $"[HORUS RTS] Failed to parse factory config: {ex.Message}. Using defaults.");
            }

            return cfg;
        }

        public static void SaveConfig(string path, RtsFactoriesConfig cfg)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"version\": 1,");
            sb.AppendLine("  \"settings\": {");
            sb.AppendLine($"    \"enableFactories\": {(cfg.settings.enableFactories ? "true" : "false")},");
            sb.AppendLine($"    \"factoryIncomeEnabled\": {(cfg.settings.factoryIncomeEnabled ? "true" : "false")},");
            sb.AppendLine($"    \"factoryProductionEnabled\": {(cfg.settings.factoryProductionEnabled ? "true" : "false")},");
            sb.AppendLine($"    \"productionConsumesBudget\": {(cfg.settings.productionConsumesBudget ? "true" : "false")},");
            sb.AppendLine($"    \"autoStartFactories\": {(cfg.settings.autoStartFactories ? "true" : "false")},");
            sb.AppendLine($"    \"maxFactoriesPerFaction\": {cfg.settings.maxFactoriesPerFaction},");
            sb.AppendLine($"    \"autoCreateFactoryForAirbaseUnits\": {(cfg.settings.autoCreateFactoryForAirbaseUnits ? "true" : "false")},");
            sb.AppendLine($"    \"autoCreateFactoryForCarriers\": {(cfg.settings.autoCreateFactoryForCarriers ? "true" : "false")}");
            sb.AppendLine("  },");
            sb.AppendLine("  \"factoryPresets\": {");

            for (int i = 0; i < cfg.factoryPresets.Count; i++)
            {
                var preset = cfg.factoryPresets[i];
                sb.AppendLine($"    \"{EscapeJson(preset.presetName)}\": {{");
                sb.AppendLine($"      \"type\": \"{EscapeJson(preset.type)}\",");
                sb.AppendLine($"      \"incomePerMinute\": {preset.incomePerMinute},");
                sb.AppendLine($"      \"produceUnits\": {(preset.produceUnits ? "true" : "false")},");
                sb.AppendLine($"      \"productionIntervalSeconds\": {preset.productionIntervalSeconds},");
                sb.AppendLine($"      \"maxActiveProducedUnits\": {preset.maxActiveProducedUnits},");
                sb.AppendLine($"      \"visualBuilding\": \"{EscapeJson(preset.visualBuilding)}\",");
                sb.Append("      \"productionUnitKeys\": [");
                if (preset.productionUnitKeys != null && preset.productionUnitKeys.Count > 0)
                {
                    for (int j = 0; j < preset.productionUnitKeys.Count; j++)
                    {
                        sb.Append($"\"{EscapeJson(preset.productionUnitKeys[j])}\"");
                        if (j < preset.productionUnitKeys.Count - 1) sb.Append(", ");
                    }
                }
                sb.AppendLine("]");
                sb.Append("    }");
                if (i < cfg.factoryPresets.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString());
        }

        private static string EscapeJson(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public void SaveInstances()
        {
            if (!CanMutateFactories("save factories")) return;
            SaveInstancesInternal();
        }

        private void SaveInstancesInternal()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var serializableList = new SerializableFactoryList();
                foreach (var f in activeFactories)
                {
                    serializableList.factories.Add(FromRuntime(f));
                }
                string json = JsonUtility.ToJson(serializableList, true);
                File.WriteAllText(InstancesPath, json);
                HorusLog.Info("Factory", $"[HORUS RTS] Factory saved: count={activeFactories.Count}");
            }
            catch (Exception ex)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Failed to save factory instances: {ex.Message}");
            }
        }

        public void LoadInstances()
        {
            if (!CanMutateFactories("load factories")) return;
            LoadInstancesInternal();
            RecreateVirtualFactoryAnchors();
        }

        private void LoadInstancesInternal()
        {
            activeFactories.Clear();
            if (!File.Exists(InstancesPath)) return;
            try
            {
                string json = File.ReadAllText(InstancesPath);
                var list = JsonUtility.FromJson<SerializableFactoryList>(json);
                if (list != null && list.factories != null)
                {
                    foreach (var sf in list.factories)
                    {
                        var factory = ToRuntime(sf);
                        NormalizeLoadedFactory(factory);
                        activeFactories.Add(factory);
                    }
                    HorusLog.Info("Factory", $"[HORUS RTS] Factory loaded: count={activeFactories.Count}");
                }
            }
            catch (Exception ex)
            {
                HorusLog.Error("Factory", $"[HORUS RTS] Failed to load factory instances: {ex.Message}");
            }
        }

        private void NormalizeLoadedFactory(RtsFactory factory)
        {
            if (factory == null) return;
            if (string.IsNullOrEmpty(factory.presetName)) factory.presetName = factory.displayName;
            if (string.IsNullOrEmpty(factory.displayName)) factory.displayName = factory.presetName;
            if (factory.productionUnitKeys == null) factory.productionUnitKeys = new List<string>();
            if (factory.activeProducedUnits == null) factory.activeProducedUnits = new List<Unit>();

            var preset = config?.factoryPresets?.FirstOrDefault(p => string.Equals(p.presetName, factory.presetName ?? factory.displayName, StringComparison.OrdinalIgnoreCase));
            if (preset != null && string.IsNullOrEmpty(factory.visualBuilding))
            {
                factory.visualBuilding = preset.visualBuilding;
            }

            if (!factory.isVirtual && !string.IsNullOrEmpty(factory.anchorUnitName) && factory.anchorUnit == null)
            {
                factory.enabled = false;
                factory.anchorDestroyed = true;
                factory.lastStatus = "Attached anchor is missing";
                HorusLog.Warning("Factory", $"[HORUS RTS] Factory loaded inactive because attached anchor is missing: {factory.displayName} anchor={factory.anchorUnitName}");
            }
            if (!CanUseFactoryFaction(factory.factionId, out string factionReason))
            {
                factory.enabled = false;
                factory.lastStatus = factionReason;
                HorusLog.Warning("Factory", $"[HORUS RTS] Factory loaded inactive: {factory.displayName}: {factionReason}");
            }
        }

        // ─── Serialization Mappers ───────────────────────────────────────────────

        private static SerializableFactory FromRuntime(RtsFactory f)
        {
            return new SerializableFactory
            {
                id = f.id,
                displayName = f.displayName,
                presetName = string.IsNullOrEmpty(f.presetName) ? f.displayName : f.presetName,
                factionId = f.factionId,
                factionName = f.factionName,
                factoryType = f.factoryType.ToString(),
                globalX = f.globalX,
                globalY = f.globalY,
                globalZ = f.globalZ,
                yaw = f.yaw,
                enabled = f.enabled,
                generateIncome = f.generateIncome,
                incomePerMinute = f.incomePerMinute,
                produceUnits = f.produceUnits,
                consumeBudgetForProduction = f.consumeBudgetForProduction,
                productionUnitKeys = f.productionUnitKeys != null ? new List<string>(f.productionUnitKeys) : new List<string>(),
                currentProductionIndex = f.currentProductionIndex,
                productionIntervalSeconds = f.productionIntervalSeconds,
                productionTimer = f.productionTimer,
                maxActiveProducedUnits = f.maxActiveProducedUnits,
                useRallyPoint = f.useRallyPoint,
                rallyX = f.rallyX,
                rallyY = f.rallyY,
                rallyZ = f.rallyZ,
                spawnRadius = f.spawnRadius,
                anchorUnitName = f.anchorUnit == null ? f.anchorUnitName : f.anchorUnit.unitName,
                anchorDestroyed = f.anchorDestroyed,
                isVirtual = f.isVirtual,
                visualBuilding = f.visualBuilding
            };
        }

        private static RtsFactory ToRuntime(SerializableFactory sf)
        {
            RtsFactoryType type = RtsFactoryType.Economy;
            Enum.TryParse(sf.factoryType, out type);

            var f = new RtsFactory
            {
                id = sf.id,
                displayName = sf.displayName,
                presetName = string.IsNullOrEmpty(sf.presetName) ? sf.displayName : sf.presetName,
                factionId = sf.factionId,
                factionName = sf.factionName,
                factoryType = type,
                globalX = sf.globalX,
                globalY = sf.globalY,
                globalZ = sf.globalZ,
                yaw = sf.yaw,
                enabled = sf.enabled,
                generateIncome = sf.generateIncome,
                incomePerMinute = sf.incomePerMinute,
                produceUnits = sf.produceUnits,
                consumeBudgetForProduction = sf.consumeBudgetForProduction,
                productionUnitKeys = sf.productionUnitKeys != null ? new List<string>(sf.productionUnitKeys) : new List<string>(),
                currentProductionIndex = sf.currentProductionIndex,
                productionIntervalSeconds = sf.productionIntervalSeconds,
                productionTimer = sf.productionTimer,
                maxActiveProducedUnits = sf.maxActiveProducedUnits,
                useRallyPoint = sf.useRallyPoint,
                rallyX = sf.rallyX,
                rallyY = sf.rallyY,
                rallyZ = sf.rallyZ,
                spawnRadius = sf.spawnRadius,
                anchorUnitName = sf.anchorUnitName,
                anchorDestroyed = sf.anchorDestroyed,
                isVirtual = sf.isVirtual,
                visualBuilding = sf.visualBuilding
            };

            if (!string.IsNullOrEmpty(f.anchorUnitName))
            {
                f.anchorUnit = FindUnitByName(f.anchorUnitName);
            }
            return f;
        }

        private static Unit FindUnitByName(string name)
        {
            if (UnitRegistry.allUnits == null || string.IsNullOrEmpty(name)) return null;
            foreach (var unit in UnitRegistry.allUnits)
            {
                if (unit != null && unit.unitName == name) return unit;
            }
            return null;
        }

        private static List<FactoryPreset> GetDefaultPresets()
        {
            return new List<FactoryPreset>
            {
                new FactoryPreset
                {
                    presetName = "Economy Outpost",
                    type = "Economy",
                    incomePerMinute = 300,
                    produceUnits = false,
                    productionIntervalSeconds = 0,
                    maxActiveProducedUnits = 0,
                    productionUnitKeys = new List<string>(),
                    visualBuilding = "Storage Tank"
                },
                new FactoryPreset
                {
                    presetName = "Ground Vehicle Factory",
                    type = "GroundProduction",
                    incomePerMinute = 100,
                    produceUnits = true,
                    productionIntervalSeconds = 90,
                    maxActiveProducedUnits = 10,
                    productionUnitKeys = new List<string> { "Nailer" },
                    visualBuilding = "Large Factory"
                },
                new FactoryPreset
                {
                    presetName = "Airbase Production",
                    type = "AirProduction",
                    incomePerMinute = 150,
                    produceUnits = true,
                    productionIntervalSeconds = 120,
                    maxActiveProducedUnits = 6,
                    productionUnitKeys = new List<string> { "Cricket" },
                    visualBuilding = "Medium Aircraft Hangar"
                },
                new FactoryPreset
                {
                    presetName = "Naval Yard",
                    type = "NavalProduction",
                    incomePerMinute = 200,
                    produceUnits = true,
                    productionIntervalSeconds = 180,
                    maxActiveProducedUnits = 4,
                    productionUnitKeys = new List<string> { "Goldfinch" },
                    visualBuilding = "Large Factory"
                },
                new FactoryPreset
                {
                    presetName = "Defense Battery",
                    type = "DefenseProduction",
                    incomePerMinute = 50,
                    produceUnits = true,
                    productionIntervalSeconds = 100,
                    maxActiveProducedUnits = 8,
                    productionUnitKeys = new List<string> { "23mm AAA Emplacement", "IRM-S1 Emplacement", "AT-145 Emplacement", "Guard Tower", "Pillbox", "Radar Station" },
                    visualBuilding = "Radar Station"
                },
                new FactoryPreset
                {
                    presetName = "Mixed Production",
                    type = "MixedProduction",
                    incomePerMinute = 75,
                    produceUnits = true,
                    productionIntervalSeconds = 110,
                    maxActiveProducedUnits = 8,
                    productionUnitKeys = new List<string> { "Nailer", "Cricket", "Goldfinch", "23mm AAA Emplacement", "IRM-S1 Emplacement", "Guard Tower" },
                    visualBuilding = "Large Factory"
                }
            };
        }
    }
}
