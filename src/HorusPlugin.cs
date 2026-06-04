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
        public const string PluginVersion = "1.1.0";

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

        private void Awake()
        {
            Logger = base.Logger;
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

            var go = new GameObject("HorusModManager");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Core.HorusManager>();
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
