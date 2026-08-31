using System;
using System.Collections.Generic;
using HarmonyLib;
using HorusMod.Logging;

namespace HorusMod.Interaction
{
    internal static class HorusTacticalHarmonyPatches
    {
        public static void Apply(Harmony harmony)
        {
            Patch(harmony,
                AccessTools.Method(typeof(CombatAI), nameof(CombatAI.ChooseHQTarget), new[] { typeof(Unit), typeof(float), typeof(List<WeaponStation>) }),
                null,
                AccessTools.Method(typeof(HorusTacticalHarmonyPatches), nameof(ChooseHQTargetPostfix)),
                "CombatAI.ChooseHQTarget");
            Patch(harmony,
                AccessTools.Method(typeof(WeaponStation), nameof(WeaponStation.Fire), new[] { typeof(Unit), typeof(Unit) }),
                AccessTools.Method(typeof(HorusTacticalHarmonyPatches), nameof(WeaponStationFirePrefix)),
                null,
                "WeaponStation.Fire");
            Patch(harmony,
                AccessTools.Method(typeof(WeaponStation), nameof(WeaponStation.LaunchMount), new[] { typeof(Unit), typeof(Unit), typeof(GlobalPosition) }),
                AccessTools.Method(typeof(HorusTacticalHarmonyPatches), nameof(WeaponStationLaunchPrefix)),
                null,
                "WeaponStation.LaunchMount");
            Patch(harmony,
                AccessTools.Method(typeof(WeaponStation), nameof(WeaponStation.RemoteFireAuto), new[] { typeof(Unit) }),
                AccessTools.Method(typeof(HorusTacticalHarmonyPatches), nameof(WeaponStationRemoteFirePrefix)),
                null,
                "WeaponStation.RemoteFireAuto");
            Patch(harmony,
                AccessTools.Method(typeof(WeaponStation), nameof(WeaponStation.RemoteFireSingle), new[] { typeof(Unit) }),
                AccessTools.Method(typeof(HorusTacticalHarmonyPatches), nameof(WeaponStationRemoteFirePrefix)),
                null,
                "WeaponStation.RemoteFireSingle");
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodInfo original,
            System.Reflection.MethodInfo prefix, System.Reflection.MethodInfo postfix, string label)
        {
            if (original == null)
            {
                HorusLog.Warning("Tactical", $"{label} signature not found; related Horus behavior will fail open.");
                return;
            }
            harmony.Patch(original,
                prefix != null ? new HarmonyMethod(prefix) : null,
                postfix != null ? new HarmonyMethod(postfix) : null);
            HorusLog.Info("Tactical", $"{label} tactical patch applied.");
        }

        public static void ChooseHQTargetPostfix(Unit searcher, List<WeaponStation> stationList,
            ref CombatAI.TargetSearchResults __result)
        {
            if (!HorusPlugin.IsRuntimeEnabled) return;
            try
            {
                HorusTacticalOrderService.OverrideForcedTarget(searcher, stationList, ref __result);
            }
            catch (Exception ex)
            {
                HorusLog.Once("Tactical", "ForcedTargetFailure", "Forced target override failed open: " + ex.Message);
            }
        }

        public static bool WeaponStationFirePrefix(Unit owner)
        {
            if (!HorusPlugin.IsRuntimeEnabled) return true;
            return !HorusTacticalOrderService.IsFireSuppressed(owner);
        }

        public static bool WeaponStationLaunchPrefix(Unit owner)
        {
            if (!HorusPlugin.IsRuntimeEnabled) return true;
            return !HorusTacticalOrderService.IsFireSuppressed(owner);
        }

        public static bool WeaponStationRemoteFirePrefix(Unit owner)
        {
            if (!HorusPlugin.IsRuntimeEnabled) return true;
            return !HorusTacticalOrderService.IsFireSuppressed(owner);
        }
    }
}
