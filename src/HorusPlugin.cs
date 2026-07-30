using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HorusMod.Logging;
using HorusMod.Data;
using HorusMod.Compat;
using UnityEngine;

namespace HorusMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class HorusPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.reaperalan.horusmod";
        public const string PluginName = "Horus Mod Starter";
        public const string PluginVersion = "1.2.3";

        public static new ManualLogSource Logger { get; private set; }
        public static ConfigEntry<KeyCode> HotkeyToggleMode { get; private set; }
        public static ConfigEntry<KeyCode> HotkeyToggleUI { get; private set; }
        public static ConfigEntry<KeyCode> HotkeyDeselectUnit { get; private set; }
        public static ConfigEntry<float> UIScale { get; private set; }
        public static ConfigEntry<float> AltitudeStep { get; private set; }
        public static ConfigEntry<float> AltitudeStepLarge { get; private set; }
        public static ConfigEntry<float> RotationStep { get; private set; }
        public static ConfigEntry<float> RotationStepLarge { get; private set; }
        public static ConfigEntry<bool> EnableGhostPreview { get; private set; }
        public static ConfigEntry<bool> AllowScrollWhileMapOpen { get; private set; }
        public static ConfigEntry<bool> InvertScrollDirection { get; private set; }
        public static ConfigEntry<bool> AllowDeletingNonHorusUnits { get; private set; }
        public static ConfigEntry<bool> AutoOceanSnapForShips { get; private set; }
        public static ConfigEntry<bool> OceanSnapActive { get; private set; }
        public static ConfigEntry<float> OceanHeightOverride { get; private set; }
        public static ConfigEntry<float> DeleteRange { get; private set; }
        public static ConfigEntry<bool> CreditKillsToSpawner { get; private set; }
        public static ConfigEntry<bool> SpawnGroundUnitsStationary { get; private set; }
        public static ConfigEntry<bool> EnableGroupSpawn { get; private set; }
        public static ConfigEntry<float> ShipSpawnLift { get; private set; }
        public static ConfigEntry<bool> StabilizeShipsAfterSpawn { get; private set; }
        public static ConfigEntry<bool> AllowDeletingOriginalMissionUnits { get; private set; }

        // RTS Commander Mode settings
        public static ConfigEntry<bool> AllowGroupPurchasesInRtsMode { get; private set; }
        public static ConfigEntry<bool> AllowSceneryPurchasesInRts { get; private set; }
        public static ConfigEntry<bool> AllowBuildingPurchasesInRts { get; private set; }
        public static ConfigEntry<bool> RequireDeploymentConfirmation { get; private set; }
        public static ConfigEntry<bool> AutoDisarmAfterPurchase { get; private set; }
        public static ConfigEntry<bool> EnableRtsIncome { get; private set; }
        public static ConfigEntry<bool> EnableRtsUnitCap { get; private set; }
        public static ConfigEntry<bool> SyncWithFactionBudget { get; private set; }
        public static ConfigEntry<bool> EnableStrictBaseDeployment { get; private set; }
        public static ConfigEntry<float> BaseDeploymentRadius { get; private set; }

        public static ConfigEntry<HorusLogLevel> LogVerbosity { get; private set; }
        public static ConfigEntry<bool> ShowDebugTab { get; private set; }

        private void Awake()
        {
            Logger = base.Logger;
            HorusLog.Info("Bootstrap", $"[Horus:Bootstrap] Horus Mod v{PluginVersion} loaded at {DateTime.Now:O}");
            try
            {
                var assemblyPath = typeof(HorusPlugin).Assembly.Location;
                var hash = GetFileHash(assemblyPath);
                HorusLog.Info("Bootstrap", $"[HORUS BUILD CHECK] Path: {assemblyPath} | SHA256: {hash}");
            }
            catch (Exception ex)
            {
                HorusLog.Info("Bootstrap", $"[HORUS BUILD CHECK] Path retrieval failed: {ex.Message}");
            }

            HorusLog.Info("Bootstrap", $"{PluginName} v{PluginVersion}: AWAKE CALLED. Bootstrapping.");

            // Bind configurations
            HotkeyToggleMode = Config.Bind("Controls", "ToggleHorusMode", KeyCode.F9, "Key to toggle Horus Mode");
            HotkeyToggleUI = Config.Bind("Controls", "ToggleUI", KeyCode.F10, "Key to toggle the UI");
            HotkeyDeselectUnit = Config.Bind("Controls", "DeselectUnit", KeyCode.Backspace, "Key to deselect the currently selected unit");
            UIScale = Config.Bind("UI", "UIScale", 1.0f, "Scale factor for the Horus UI (e.g. 1.0 for 1080p, 1.5 for 1440p, 2.0 for 4K)");
            AltitudeStep = Config.Bind("Placement", "AltitudeStep", 50f, "Altitude change per scroll tick with Ctrl+Scroll (meters)");
            AltitudeStepLarge = Config.Bind("Placement", "AltitudeStepLarge", 500f, "Altitude change per scroll tick with Ctrl+Shift+Scroll (meters)");
            RotationStep = Config.Bind("Placement", "RotationStep", 5f, "Yaw change per scroll tick with Alt+Scroll (degrees)");
            RotationStepLarge = Config.Bind("Placement", "RotationStepLarge", 45f, "Yaw change per scroll tick with Alt+Shift+Scroll (degrees)");
            EnableGhostPreview = Config.Bind("Placement", "EnableGhostPreview", true, "Show a local-only ghost/preview of the selected unit before spawning");
            AllowScrollWhileMapOpen = Config.Bind("Placement", "AllowScrollWhileMapOpen", true, "Allow Ctrl/Alt+Scroll altitude/yaw shortcuts while the map is open (may also zoom the map)");
            InvertScrollDirection = Config.Bind("Placement", "InvertScrollDirection", false, "Invert the scroll wheel direction for altitude/yaw shortcuts");
            AllowDeletingNonHorusUnits = Config.Bind("Safety", "AllowDeletingNonHorusUnits", false, "If true, middle-click can delete real gameplay units NOT spawned by Horus (still never terrain/roads/map geometry/original-map units). Default false = only delete units spawned by Horus this session.");
            AutoOceanSnapForShips = Config.Bind("Placement", "AutoOceanSnapForShips", true, "Automatically snap ships/ocean units to the water level (sea level).");
            OceanSnapActive = Config.Bind("Placement", "OceanSnapActive", false, "Manually force placement of all units to snap to the ocean level.");
            OceanHeightOverride = Config.Bind("Placement", "OceanHeightOverride", -9999f, "Manual override for the ocean level height. If -9999 (default), it uses the game's sea level.");
            DeleteRange = Config.Bind("Safety", "DeleteRange", 50f, "The search radius around the cursor hit point to find units when middle-clicking to delete (meters).");
            CreditKillsToSpawner = Config.Bind("Multiplayer", "CreditKillsToSpawner", false, "[Experimental] Try to credit kills made by Horus spawned units to the player who spawned them. Note: May have side effects.");
            SpawnGroundUnitsStationary = Config.Bind("Groups", "SpawnGroundUnitsStationary", false, "Spawn ground vehicles/ships in a stationary/parked state (hold position).");
            EnableGroupSpawn = Config.Bind("Groups", "EnableGroupSpawn", false, "Enable spawning groups of units in formation instead of single units.");
            ShipSpawnLift = Config.Bind("Placement", "ShipSpawnLift", 3f, "Extra elevation lift for safe ship spawning to prevent dragging on the seabed (meters).");
            StabilizeShipsAfterSpawn = Config.Bind("Placement", "StabilizeShipsAfterSpawn", true, "Force-stabilize ship transforms and velocities for a few physics frames after spawning.");
            AllowDeletingOriginalMissionUnits = Config.Bind("Safety", "AllowDeletingOriginalMissionUnits", false, "If true, middle-click can delete original mission units (builtin map units).");
            // RTS Commander Mode config bindings
            AllowGroupPurchasesInRtsMode = Config.Bind("RTS", "AllowGroupPurchasesInRtsMode", false, "If true, allow group spawning in RTS Commander Mode (costs the sum of all units).");
            AllowSceneryPurchasesInRts = Config.Bind("RTS", "AllowSceneryPurchasesInRts", false, "If true, scenery objects cost budget in RTS Mode. If false, scenery is blocked.");
            AllowBuildingPurchasesInRts = Config.Bind("RTS", "AllowBuildingPurchasesInRts", true, "If true, buildings can be purchased in RTS Mode.");
            RequireDeploymentConfirmation = Config.Bind("RTS", "RequireDeploymentConfirmation", true, "If true, spawning in RTS Mode requires arming the deployment first (two-step).");
            AutoDisarmAfterPurchase = Config.Bind("RTS", "AutoDisarmAfterPurchase", true, "If true, deployment is automatically disarmed after a successful spawn in RTS Mode.");
            EnableRtsIncome = Config.Bind("RTS", "EnableRtsIncome", true, "If true, factions receive passive income every tick in RTS Mode.");
            EnableRtsUnitCap = Config.Bind("RTS", "EnableRtsUnitCap", true, "If true, enforce per-faction unit caps in RTS Mode.");
            SyncWithFactionBudget = Config.Bind("RTS", "SyncWithFactionBudget", false, "If true, the RTS budget is synced with the actual in-game faction budget instead of local Horus budget.");
            EnableStrictBaseDeployment = Config.Bind("RTS", "EnableStrictBaseDeployment", false, "If true, units can only be deployed within BaseDeploymentRadius of a friendly building/carrier.");
            BaseDeploymentRadius = Config.Bind("RTS", "BaseDeploymentRadius", 3000f, "Radius in meters for strict base deployment restriction.");
            LogVerbosity = Config.Bind("Diagnostics", "LogVerbosity", HorusLogLevel.Normal, "Quiet, Normal, Verbose, or Trace.");
            ShowDebugTab = Config.Bind("Diagnostics", "ShowDebugTab", false, "Show the Debug tab and self-test diagnostics.");
            HorusPrefs.Bind(Config);
            GameApi.Initialize();

            try
            {
                // Patch CameraFreeState to prevent input fighting
                var harmony = new HarmonyLib.Harmony(PluginGuid);
                var original = typeof(CameraFreeState).GetMethod(nameof(CameraFreeState.UpdateState));
                
                if (original != null)
                {
                    var prefix = typeof(CameraFreeStatePatch).GetMethod(nameof(CameraFreeStatePatch.Prefix));
                    harmony.Patch(original, new HarmonyLib.HarmonyMethod(prefix));
                    HorusLog.Info("Bootstrap", $"{PluginName}: Harmony patch applied successfully.");
                }
                else
                {
                    HorusLog.Error("Bootstrap", $"{PluginName}: Could not find CameraFreeState.UpdateState. Game may have updated.");
                }
            }
            catch (Exception ex)
            {
                HorusLog.Error("Bootstrap", $"{PluginName}: Failed to apply Harmony patch. Exception: {ex.Message}");
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

}
