using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace HorusMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class HorusPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.reaperalan.horusmod";
        public const string PluginName = "Horus Mod Starter";
        public const string PluginVersion = "1.2.0";

        public static new ManualLogSource Logger { get; private set; }
        public static ConfigEntry<KeyCode> HotkeyToggleMode { get; private set; }
        public static ConfigEntry<KeyCode> HotkeyToggleUI { get; private set; }
        public static ConfigEntry<float> AltitudeStep { get; private set; }
        public static ConfigEntry<float> AltitudeStepLarge { get; private set; }
        public static ConfigEntry<float> RotationStep { get; private set; }
        public static ConfigEntry<float> RotationStepLarge { get; private set; }
        public static ConfigEntry<bool> EnableGhostPreview { get; private set; }
        public static ConfigEntry<bool> AllowScrollWhileMapOpen { get; private set; }
        public static ConfigEntry<bool> InvertScrollDirection { get; private set; }
        public static ConfigEntry<bool> AllowDeletingNonHorusUnits { get; private set; }
        public static ConfigEntry<bool> AllowClientHorusRequests { get; private set; }
        public static ConfigEntry<bool> EnableExperimentalWhitelist { get; private set; }
        public static ConfigEntry<bool> AutoOceanSnapForShips { get; private set; }
        public static ConfigEntry<bool> OceanSnapActive { get; private set; }
        public static ConfigEntry<float> OceanHeightOverride { get; private set; }
        public static ConfigEntry<float> DeleteRange { get; private set; }
        public static ConfigEntry<bool> SpawnGroundUnitsStationary { get; private set; }
        public static ConfigEntry<bool> EnableGroupSpawn { get; private set; }
        public static ConfigEntry<float> ShipSpawnLift { get; private set; }
        public static ConfigEntry<bool> StabilizeShipsAfterSpawn { get; private set; }
        public static ConfigEntry<bool> AllowDeletingOriginalMissionUnits { get; private set; }
        public static ConfigEntry<float> StartingBudgetPrimeva { get; private set; }
        public static ConfigEntry<float> StartingBudgetBoscali { get; private set; }

        // RTS Commander Mode settings
        public static ConfigEntry<bool> AllowGroupPurchasesInRtsMode { get; private set; }
        public static ConfigEntry<bool> AllowSceneryPurchasesInRts { get; private set; }
        public static ConfigEntry<bool> AllowBuildingPurchasesInRts { get; private set; }
        public static ConfigEntry<bool> RequireDeploymentConfirmation { get; private set; }
        public static ConfigEntry<bool> AutoDisarmAfterPurchase { get; private set; }
        public static ConfigEntry<bool> EnableRtsIncome { get; private set; }
        public static ConfigEntry<float> IncomeTickSeconds { get; private set; }
        public static ConfigEntry<bool> EnableRtsUnitCap { get; private set; }
        public static ConfigEntry<bool> EnableStrictBaseDeployment { get; private set; }
        public static ConfigEntry<float> BaseDeploymentRadius { get; private set; }

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo("[HORUS BUILD CHECK] Horus Mod v1.2.0 ship-debug build loaded at " + DateTime.Now);
            try
            {
                var assemblyPath = typeof(HorusPlugin).Assembly.Location;
                var hash = GetFileHash(assemblyPath);
                Logger.LogInfo($"[HORUS BUILD CHECK] Path: {assemblyPath} | SHA256: {hash}");
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"[HORUS BUILD CHECK] Path retrieval failed: {ex.Message}");
            }

            Logger.LogInfo($"{PluginName} v{PluginVersion}: AWAKE CALLED. Bootstrapping.");

            // Bind configurations
            HotkeyToggleMode = Config.Bind("Controls", "ToggleHorusMode", KeyCode.F9, "Key to toggle Horus Mode");
            HotkeyToggleUI = Config.Bind("Controls", "ToggleUI", KeyCode.F10, "Key to toggle the UI");
            AltitudeStep = Config.Bind("Placement", "AltitudeStep", 50f, "Altitude change per scroll tick with Ctrl+Scroll (meters)");
            AltitudeStepLarge = Config.Bind("Placement", "AltitudeStepLarge", 500f, "Altitude change per scroll tick with Ctrl+Shift+Scroll (meters)");
            RotationStep = Config.Bind("Placement", "RotationStep", 5f, "Yaw change per scroll tick with Alt+Scroll (degrees)");
            RotationStepLarge = Config.Bind("Placement", "RotationStepLarge", 45f, "Yaw change per scroll tick with Alt+Shift+Scroll (degrees)");
            EnableGhostPreview = Config.Bind("Placement", "EnableGhostPreview", true, "Show a local-only ghost/preview of the selected unit before spawning");
            AllowScrollWhileMapOpen = Config.Bind("Placement", "AllowScrollWhileMapOpen", true, "Allow Ctrl/Alt+Scroll altitude/yaw shortcuts while the map is open (may also zoom the map)");
            InvertScrollDirection = Config.Bind("Placement", "InvertScrollDirection", false, "Invert the scroll wheel direction for altitude/yaw shortcuts");
            AllowDeletingNonHorusUnits = Config.Bind("Safety", "AllowDeletingNonHorusUnits", false, "If true, middle-click can delete real gameplay units NOT spawned by Horus (still never terrain/roads/map geometry/original-map units). Default false = only delete units spawned by Horus this session.");
            AllowClientHorusRequests = Config.Bind("Multiplayer", "AllowClientHorusRequests", false, "Reserved: allow whitelisted multiplayer clients to request Horus actions (experimental, host-validated)");
            EnableExperimentalWhitelist = Config.Bind("Multiplayer", "EnableExperimentalWhitelist", false, "Reserved: enable the experimental host-side client whitelist (planned)");
            AutoOceanSnapForShips = Config.Bind("Placement", "AutoOceanSnapForShips", true, "Automatically snap ships/ocean units to the water level (sea level).");
            OceanSnapActive = Config.Bind("Placement", "OceanSnapActive", false, "Manually force placement of all units to snap to the ocean level.");
            OceanHeightOverride = Config.Bind("Placement", "OceanHeightOverride", -9999f, "Manual override for the ocean level height. If -9999 (default), it uses the game's sea level.");
            DeleteRange = Config.Bind("Safety", "DeleteRange", 50f, "The search radius around the cursor hit point to find units when middle-clicking to delete (meters).");
            SpawnGroundUnitsStationary = Config.Bind("Groups", "SpawnGroundUnitsStationary", false, "Spawn ground vehicles/ships in a stationary/parked state (hold position).");
            EnableGroupSpawn = Config.Bind("Groups", "EnableGroupSpawn", false, "Enable spawning groups of units in formation instead of single units.");
            ShipSpawnLift = Config.Bind("Placement", "ShipSpawnLift", 3f, "Extra elevation lift for safe ship spawning to prevent dragging on the seabed (meters).");
            StabilizeShipsAfterSpawn = Config.Bind("Placement", "StabilizeShipsAfterSpawn", true, "Force-stabilize ship transforms and velocities for a few physics frames after spawning.");
            AllowDeletingOriginalMissionUnits = Config.Bind("Safety", "AllowDeletingOriginalMissionUnits", false, "If true, middle-click can delete original mission units (builtin map units).");
            StartingBudgetPrimeva = Config.Bind("Budget", "StartingBudgetPrimeva", 5000f, "Starting budget for Primeva in RTS/Budget Mode");
            StartingBudgetBoscali = Config.Bind("Budget", "StartingBudgetBoscali", 5000f, "Starting budget for Boscali in RTS/Budget Mode");

            // RTS Commander Mode config bindings
            AllowGroupPurchasesInRtsMode = Config.Bind("RTS", "AllowGroupPurchasesInRtsMode", false, "If true, allow group spawning in RTS Commander Mode (costs the sum of all units).");
            AllowSceneryPurchasesInRts = Config.Bind("RTS", "AllowSceneryPurchasesInRts", false, "If true, scenery objects cost budget in RTS Mode. If false, scenery is blocked.");
            AllowBuildingPurchasesInRts = Config.Bind("RTS", "AllowBuildingPurchasesInRts", true, "If true, buildings can be purchased in RTS Mode.");
            RequireDeploymentConfirmation = Config.Bind("RTS", "RequireDeploymentConfirmation", true, "If true, spawning in RTS Mode requires arming the deployment first (two-step).");
            AutoDisarmAfterPurchase = Config.Bind("RTS", "AutoDisarmAfterPurchase", true, "If true, deployment is automatically disarmed after a successful spawn in RTS Mode.");
            EnableRtsIncome = Config.Bind("RTS", "EnableRtsIncome", true, "If true, factions receive passive income every tick in RTS Mode.");
            IncomeTickSeconds = Config.Bind("RTS", "IncomeTickSeconds", 5.0f, "Seconds between income ticks in RTS Mode.");
            EnableRtsUnitCap = Config.Bind("RTS", "EnableRtsUnitCap", true, "If true, enforce per-faction unit caps in RTS Mode.");
            EnableStrictBaseDeployment = Config.Bind("RTS", "EnableStrictBaseDeployment", false, "If true, units can only be deployed within BaseDeploymentRadius of a friendly building/carrier.");
            BaseDeploymentRadius = Config.Bind("RTS", "BaseDeploymentRadius", 3000f, "Radius in meters for strict base deployment restriction.");

            try
            {
                // Patch CameraFreeState to prevent input fighting
                var harmony = new HarmonyLib.Harmony(PluginGuid);
                var original = typeof(CameraFreeState).GetMethod(nameof(CameraFreeState.UpdateState));
                
                if (original != null)
                {
                    var prefix = typeof(CameraFreeStatePatch).GetMethod(nameof(CameraFreeStatePatch.Prefix));
                    harmony.Patch(original, new HarmonyLib.HarmonyMethod(prefix));
                    Logger.LogInfo($"{PluginName}: Harmony patch applied successfully.");
                }
                else
                {
                    Logger.LogError($"{PluginName}: Could not find CameraFreeState.UpdateState. Game may have updated.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"{PluginName}: Failed to apply Harmony patch. Exception: {ex.Message}");
            }

            // Apply diagnostics Harmony patches
            try
            {
                var diagHarmony = new HarmonyLib.Harmony(PluginGuid + ".diagnostics");

                // Patch Unit.ReportKilled
                var unitReportKilled = typeof(Unit).GetMethod("ReportKilled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (unitReportKilled != null)
                {
                    var prefix = typeof(UnitDiagnosticsPatch).GetMethod(nameof(UnitDiagnosticsPatch.PrefixReportKilled));
                    diagHarmony.Patch(unitReportKilled, new HarmonyLib.HarmonyMethod(prefix));
                    Logger.LogInfo("[HORUS DIAGNOSTICS] Patched Unit.ReportKilled successfully.");
                }
                else
                {
                    Logger.LogWarning("[HORUS DIAGNOSTICS] Could not find Unit.ReportKilled");
                }

                // Patch Ship.ReportKilled
                var shipReportKilled = typeof(Ship).GetMethod("ReportKilled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (shipReportKilled != null)
                {
                    var prefix = typeof(UnitDiagnosticsPatch).GetMethod(nameof(UnitDiagnosticsPatch.PrefixShipReportKilled));
                    diagHarmony.Patch(shipReportKilled, new HarmonyLib.HarmonyMethod(prefix));
                    Logger.LogInfo("[HORUS DIAGNOSTICS] Patched Ship.ReportKilled successfully.");
                }
                else
                {
                    Logger.LogWarning("[HORUS DIAGNOSTICS] Could not find Ship.ReportKilled");
                }

                // Patch Ship.CheckShipBuoyancy
                var checkShipBuoyancy = typeof(Ship).GetMethod("CheckShipBuoyancy", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (checkShipBuoyancy != null)
                {
                    var prefix = typeof(UnitDiagnosticsPatch).GetMethod(nameof(UnitDiagnosticsPatch.PrefixCheckShipBuoyancy));
                    diagHarmony.Patch(checkShipBuoyancy, new HarmonyLib.HarmonyMethod(prefix));
                    Logger.LogInfo("[HORUS DIAGNOSTICS] Patched Ship.CheckShipBuoyancy successfully.");
                }
                else
                {
                    Logger.LogWarning("[HORUS DIAGNOSTICS] Could not find Ship.CheckShipBuoyancy");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[HORUS DIAGNOSTICS] Failed to apply diagnostics patches: {ex.Message}");
            }

            var go = new GameObject("HorusModManager");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Core.HorusManager>();
        }

        private static string GetFileHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return "Unknown/FileNotExists";
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                using (var stream = System.IO.File.OpenRead(filePath))
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
        }
    }

    // Harmony patch to abort CameraFreeState.UpdateState when Horus is active
    public static class CameraFreeStatePatch
    {
        public static bool Prefix()
        {
            if (Core.HorusManager.Instance != null && Core.HorusManager.Instance.IsHorusActive)
            {
                // Skip the native CameraFreeState logic to avoid input fighting and inertia
                return false;
            }
            return true; // Run normally
        }
    }

    public static class UnitDiagnosticsPatch
    {
        public static bool PrefixReportKilled(Unit __instance)
        {
            if (__instance != null && (__instance is Ship || __instance.GetComponent<Ship>() != null || __instance.GetComponentInChildren<Ship>(true) != null))
            {
                UnityEngine.Debug.LogWarning($"[HORUS SHIP DEATH] Unit.ReportKilled called for Ship '{__instance.unitName}'");
                UnityEngine.Debug.LogWarning(Environment.StackTrace);
            }
            return true;
        }

        public static bool PrefixShipReportKilled(Ship __instance)
        {
            if (__instance != null)
            {
                UnityEngine.Debug.LogWarning($"[HORUS SHIP DEATH] Ship.ReportKilled called for '{__instance.unitName}'");
                UnityEngine.Debug.LogWarning(Environment.StackTrace);
            }
            return true;
        }

        public static bool PrefixCheckShipBuoyancy(Ship __instance)
        {
            return true;
        }
    }
}
