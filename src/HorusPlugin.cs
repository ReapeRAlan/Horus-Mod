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
        public const string PluginVersion = "1.0.9";

        public static new ManualLogSource Logger { get; private set; }
        public static ConfigEntry<KeyCode> HotkeyToggleMode { get; private set; }
        public static ConfigEntry<KeyCode> HotkeyToggleUI { get; private set; }

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"{PluginName} v{PluginVersion}: AWAKE CALLED. Bootstrapping.");

            // Bind configurations
            HotkeyToggleMode = Config.Bind("Controls", "ToggleHorusMode", KeyCode.F9, "Key to toggle Horus Mode");
            HotkeyToggleUI = Config.Bind("Controls", "ToggleUI", KeyCode.F10, "Key to toggle the UI");

            // Patch CameraFreeState to prevent input fighting
            var harmony = new HarmonyLib.Harmony(PluginGuid);
            var original = typeof(CameraFreeState).GetMethod(nameof(CameraFreeState.UpdateState));
            var prefix = typeof(CameraFreeStatePatch).GetMethod(nameof(CameraFreeStatePatch.Prefix));
            harmony.Patch(original, new HarmonyLib.HarmonyMethod(prefix));
            
            Logger.LogInfo($"{PluginName}: Harmony patch applied successfully.");

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
