using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HorusMod.Logging;
using HorusMod.Data;
using HorusMod.Compat;
using HorusMod.Interaction;
using UnityEngine;

namespace HorusMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class HorusPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.reaperalan.horusmod";
        public const string PluginName = "Horus Mod Starter";
        public const string PluginVersion = "1.4.3";

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
        public static ConfigEntry<bool> AllowIncompatibleContent { get; private set; }

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
        public static ConfigEntry<bool> ImproveAIBombingAccuracy { get; private set; }

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
            AllowIncompatibleContent = Config.Bind(
                "Safety",
                "AllowIncompatibleContent",
                false,
                "Expose the force-spawn acknowledgement for Lookup-only definitions. These objects are not registered for network serialization and may desync or disconnect clients.");
            // RTS Commander Mode config bindings
            AllowGroupPurchasesInRtsMode = Config.Bind("RTS", "AllowGroupPurchasesInRtsMode", false, "If true, allow group spawning in RTS Commander Mode (costs the sum of all units).");
            AllowSceneryPurchasesInRts = Config.Bind("RTS", "AllowSceneryPurchasesInRts", false, "If true, scenery objects cost budget in RTS Mode. If false, scenery is blocked.");
            AllowBuildingPurchasesInRts = Config.Bind("RTS", "AllowBuildingPurchasesInRts", true, "If true, buildings can be purchased in RTS Mode.");
            RequireDeploymentConfirmation = Config.Bind("RTS", "RequireDeploymentConfirmation", false, "If true, spawning in RTS Mode requires arming the deployment first (two-step, 'click again to deploy'). Off by default so placement is a single click.");
            AutoDisarmAfterPurchase = Config.Bind("RTS", "AutoDisarmAfterPurchase", true, "If true, deployment is automatically disarmed after a successful spawn in RTS Mode.");
            EnableRtsIncome = Config.Bind("RTS", "EnableRtsIncome", true, "If true, factions receive passive income every tick in RTS Mode.");
            EnableRtsUnitCap = Config.Bind("RTS", "EnableRtsUnitCap", true, "If true, enforce per-faction unit caps in RTS Mode.");
            SyncWithFactionBudget = Config.Bind("RTS", "SyncWithFactionBudget", false, "If true, the RTS budget is synced with the actual in-game faction budget instead of local Horus budget.");
            EnableStrictBaseDeployment = Config.Bind("RTS", "EnableStrictBaseDeployment", false, "If true, units can only be deployed within BaseDeploymentRadius of a friendly building/carrier.");
            BaseDeploymentRadius = Config.Bind("RTS", "BaseDeploymentRadius", 3000f, "Radius in meters for strict base deployment restriction.");
            LogVerbosity = Config.Bind("Diagnostics", "LogVerbosity", HorusLogLevel.Normal, "Quiet, Normal, Verbose, or Trace.");
            ShowDebugTab = Config.Bind("Diagnostics", "ShowDebugTab", false, "Show the Debug tab and self-test diagnostics.");
            ImproveAIBombingAccuracy = Config.Bind("AI", "ImproveAIBombingAccuracy", true,
                "Correct conventional AI bomb release for target motion and weapon rail/ejection delay. Fails open to native behavior if game signatures change.");
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

            try
            {
                // Missile.LocalStart() unconditionally calls seeker.Initialize(...). Any
                // MissileDefinition with no MissileSeeker component (unguided bombs/rockets)
                // leaves `seeker` null there, which throws a NullReferenceException during
                // network spawn and can tear the object back down before it ever flies --
                // it appears for a frame and is gone, with no explosion or error toast.
                // Skip that call for unguided weapons; there is nothing to initialize.
                var harmony = new HarmonyLib.Harmony(PluginGuid);
                var missileLocalStart = HarmonyLib.AccessTools.Method(typeof(Missile), "LocalStart");
                if (missileLocalStart != null)
                {
                    var missilePrefix = typeof(MissileLocalStartPatch).GetMethod(nameof(MissileLocalStartPatch.Prefix));
                    harmony.Patch(missileLocalStart, new HarmonyLib.HarmonyMethod(missilePrefix));
                    HorusLog.Info("Bootstrap", $"{PluginName}: Missile.LocalStart safety patch applied.");
                }
                else
                {
                    HorusLog.Error("Bootstrap", $"{PluginName}: Could not find Missile.LocalStart. Game may have updated.");
                }
            }
            catch (Exception ex)
            {
                HorusLog.Error("Bootstrap", $"{PluginName}: Failed to apply Missile.LocalStart patch. Exception: {ex.Message}");
            }

            try
            {
                // ARHSeeker.SlowChecks and OpticalSeekerCruiseMissile.SlowChecks/PreTerminalMode
                // each treat "no target" as a self-destruct or divert-to-cruise-altitude trigger.
                // Native gameplay never exercises that path -- a human/AI pilot always fires these
                // at something -- but Horus's World Point Live Ordnance mode spawns with
                // target=null by default, so every shot using one of these
                // seekers was guaranteed to self-destruct within 2-10s wherever it happened to be,
                // or (for the cruise missile) climb toward cruise altitude and detonate mid-air 2km
                // short, instead of continuing the straight-down drop onto the clicked point.
                // These patches only change behavior when the seeker has no target; a real target
                // (from Track Selected / compatible Impact Selected) runs native logic unmodified.
                var harmony = new HarmonyLib.Harmony(PluginGuid);
                PatchSeekerNoTarget(harmony, typeof(ARHSeeker), "SlowChecks",
                    nameof(SeekerNoTargetPatches.ARHSeeker_SlowChecks_Prefix));
                PatchSeekerNoTarget(harmony, typeof(OpticalSeekerCruiseMissile), "PreTerminalMode",
                    nameof(SeekerNoTargetPatches.OpticalSeekerCruiseMissile_PreTerminalMode_Prefix));
                PatchSeekerNoTarget(harmony, typeof(OpticalSeekerCruiseMissile), "SlowChecks",
                    nameof(SeekerNoTargetPatches.OpticalSeekerCruiseMissile_SlowChecks_Prefix));
            }
            catch (Exception ex)
            {
                HorusLog.Error("Bootstrap", $"{PluginName}: Failed to apply seeker no-target patches. Exception: {ex.Message}");
            }

            try
            {
                var harmony = new HarmonyLib.Harmony(PluginGuid);
                HorusTacticalHarmonyPatches.Apply(harmony);
            }
            catch (Exception ex)
            {
                HorusLog.Error("Bootstrap", $"{PluginName}: Tactical patches failed open. Exception: {ex.Message}");
            }
            try
            {
                var harmony = new HarmonyLib.Harmony(PluginGuid);
                HorusBombingCorrection.Apply(harmony);
            }
            catch (Exception ex)
            {
                HorusLog.Error("Bootstrap", $"{PluginName}: AI bombing patch failed open. Exception: {ex.Message}");
            }

            var go = new GameObject("HorusModManager");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Core.HorusManager>();
        }

        private static void PatchSeekerNoTarget(HarmonyLib.Harmony harmony, Type seekerType, string methodName, string prefixMethodName)
        {
            var original = HarmonyLib.AccessTools.Method(seekerType, methodName);
            if (original == null)
            {
                HorusLog.Error("Bootstrap", $"{PluginName}: Could not find {seekerType.Name}.{methodName}. Game may have updated.");
                return;
            }
            var prefix = typeof(SeekerNoTargetPatches).GetMethod(prefixMethodName);
            harmony.Patch(original, new HarmonyLib.HarmonyMethod(prefix));
            HorusLog.Info("Bootstrap", $"{PluginName}: {seekerType.Name}.{methodName} no-target patch applied.");
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

    // Harmony patch to guard Missile.LocalStart against a null seeker on unguided munitions.
    //
    // LocalStart() unconditionally calls seeker.Initialize(...), which NullReferenceExceptions
    // when the definition has no MissileSeeker component. The original fix (skip the whole
    // method) had a side effect: it also skipped setting the private `aimPoint` field, which
    // Steering() reads EVERY FixedUpdate regardless of whether a seeker exists. With aimPoint
    // left at its default (GlobalPosition(0,0,0)), every unguided munition actively steered
    // toward the world/mission origin instead of flying straight -- explaining shots that
    // consistently drifted toward the same fixed point (whatever sits near global origin)
    // no matter where they were launched or aimed. This patch instead replicates LocalStart's
    // aimPoint computation via the public SetAimpoint(...) API and only skips the crashing
    // seeker.Initialize(...) call.
    public static class MissileLocalStartPatch
    {
        public static bool Prefix(Missile __instance)
        {
            if (__instance == null || __instance.GetComponent<MissileSeeker>() != null)
                return true; // Has a seeker: run natively.

            if (GameManager.gameState == GameState.SinglePlayer || GameManager.gameState == GameState.Multiplayer)
            {
                Unit owner = __instance.owner;
                GlobalPosition originPos = owner != null ? owner.transform.GlobalPosition() : __instance.transform.GlobalPosition();
                Vector3 forward = owner != null ? owner.transform.forward : __instance.transform.forward;
                __instance.SetAimpoint(originPos + forward * 100000f, Vector3.zero);
            }
            return false; // Skip the native seeker.Initialize(...) call.
        }
    }

    // Harmony patches guarding ARHSeeker/OpticalSeekerCruiseMissile against the "no target"
    // self-destruct/altitude-climb behavior that only makes sense when a real Unit target
    // was ever provided. `targetUnit` is a protected field declared on the shared base class
    // MissileSeeker, so it's read via reflection once and reused by every patch here.
    public static class SeekerNoTargetPatches
    {
        private static readonly System.Reflection.FieldInfo TargetUnitField =
            typeof(MissileSeeker).GetField("targetUnit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo MissileField =
            typeof(MissileSeeker).GetField("missile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static bool HasNoTarget(MissileSeeker seeker)
        {
            return seeker != null && TargetUnitField != null && TargetUnitField.GetValue(seeker) == null;
        }

        private static Missile GetMissile(MissileSeeker seeker)
        {
            return MissileField?.GetValue(seeker) as Missile;
        }

        // ARHSeeker.SlowChecks: with no target, the method's only meaningful effect is the
        // self-destruct check -- the second block requires targetUnit != null and is already
        // a no-op without one -- so it is safe to skip the entire method.
        public static bool ARHSeeker_SlowChecks_Prefix(ARHSeeker __instance)
        {
            return !HasNoTarget(__instance); // false (skip) when there's no target, else run natively.
        }

        // OpticalSeekerCruiseMissile.PreTerminalMode: with no target, its terrain-following
        // waypoint (TerrainWaypoint) forces the missile to climb toward cruise altitude, and
        // its terminal-range check detonates immediately once "close enough" to that stale
        // waypoint. Skipping the whole method leaves the missile's aimpoint at Initialize()'s
        // original (correct, straight-down) value instead.
        public static bool OpticalSeekerCruiseMissile_PreTerminalMode_Prefix(OpticalSeekerCruiseMissile __instance)
        {
            return !HasNoTarget(__instance);
        }

        // OpticalSeekerCruiseMissile.SlowChecks: unlike ARHSeeker, this method has side effects
        // that must still happen without a target (radar altitude tracking, becoming tangible).
        // Replicate those, keep every self-destruct condition except "no target", then skip
        // the native call so it can't also fire on that condition.
        public static bool OpticalSeekerCruiseMissile_SlowChecks_Prefix(OpticalSeekerCruiseMissile __instance)
        {
            if (!HasNoTarget(__instance)) return true;
            Missile missile = GetMissile(__instance);
            if (missile == null || missile.disabled) return false;

            missile.UpdateRadarAlt();
            if (missile.timeSinceSpawn > 10f && (missile.LosingGround() || missile.MissedTarget() || missile.speed < 100f))
                missile.Detonate(missile.rb.velocity, hitArmor: false, hitTerrain: false);
            if (!missile.IsTangible() && missile.timeSinceSpawn > 2f)
                missile.SetTangible(true);
            return false;
        }
    }

}
