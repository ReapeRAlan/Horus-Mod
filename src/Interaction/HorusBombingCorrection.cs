using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HorusMod.Logging;
using UnityEngine;

namespace HorusMod.Interaction
{
    /// <summary>
    /// Gates the native AI bomb-release request until a corrected ballistic
    /// solution crosses its skill-dependent, zero-mean release point. Navigation,
    /// weapon selection and break-off remain native.
    /// </summary>
    internal static class HorusBombingCorrection
    {
        private sealed class ReleasePass
        {
            public Unit Target;
            public byte Station;
            public float BiasSeconds;
        }

        private static readonly FieldInfo PlaneTargetField = AccessTools.Field(typeof(AIPilotCombatModes), "currentTarget");
        private static readonly FieldInfo HeloTargetField = AccessTools.Field(typeof(AIHeloCombatState), "currentTarget");
        private static readonly FieldInfo RailDelayField = AccessTools.Field(typeof(MountedMissile), "railDelay");
        private static readonly FieldInfo RailLengthField = AccessTools.Field(typeof(MountedMissile), "railLength");
        private static readonly FieldInfo RailSpeedField = AccessTools.Field(typeof(MountedMissile), "railSpeed");
        private static readonly Dictionary<Aircraft, ReleasePass> passes = new Dictionary<Aircraft, ReleasePass>();

        public static void Apply(Harmony harmony)
        {
            MethodInfo original = AccessTools.Method(typeof(Pilot), nameof(Pilot.Fire), Type.EmptyTypes);
            MethodInfo prefix = AccessTools.Method(typeof(HorusBombingCorrection), nameof(PilotFirePrefix));
            if (original == null || prefix == null || PlaneTargetField == null || HeloTargetField == null)
            {
                HorusLog.Warning("Bombing", "AI bombing signatures changed; accuracy correction disabled and native behavior retained.");
                return;
            }
            harmony.Patch(original, new HarmonyMethod(prefix));
            HorusLog.Info("Bombing", "Pilot.Fire ballistic release gate applied for conventional AI bombs.");
        }

        public static void Reset() => passes.Clear();

        public static bool PilotFirePrefix(Pilot __instance)
        {
            try
            {
                return ShouldAllowRelease(__instance);
            }
            catch (Exception ex)
            {
                HorusLog.Once("Bombing", "ReleaseGateFailure", "Bomb release correction failed open: " + ex.Message);
                return true;
            }
        }

        private static bool ShouldAllowRelease(Pilot pilot)
        {
            if (HorusPlugin.ImproveAIBombingAccuracy == null || !HorusPlugin.ImproveAIBombingAccuracy.Value) return true;
            Aircraft aircraft = pilot?.aircraft;
            if (aircraft == null || aircraft.Player != null || !aircraft.IsServer || aircraft.rb == null || aircraft.weaponManager == null)
                return true;
            if (HorusTacticalOrderService.IsFireSuppressed(aircraft)) return true;

            WeaponStation station = aircraft.weaponManager.currentWeaponStation;
            WeaponInfo info = station?.WeaponInfo;
            if (station == null || info == null || !info.bomb || info.glideBomb || info.laserGuided) return true;

            PilotBaseState state = pilot.currentState;
            Unit target;
            if (state is AIPilotCombatModes) target = PlaneTargetField.GetValue(state) as Unit;
            else if (state is AIHeloCombatState) target = HeloTargetField.GetValue(state) as Unit;
            else return true;
            if (target == null || target.disabled) return true;

            float launchDelay = GetLaunchDelay(station);
            Vector3 aircraftVelocity = aircraft.rb.velocity;
            Vector3 targetVelocity = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 releaseVelocity = aircraftVelocity + aircraft.transform.forward * info.muzzleVelocity;
            float height = aircraft.transform.position.y - (target.transform.position.y + info.airburstHeight);
            float fallTime = Kinematics.FallTime(height, releaseVelocity.y);
            if (float.IsNaN(fallTime) || float.IsInfinity(fallTime) || fallTime <= 0f) return true;

            Vector3 horizontalVelocity = new Vector3(releaseVelocity.x, 0f, releaseVelocity.z);
            if (horizontalVelocity.sqrMagnitude < 1f) return true;
            Vector3 direction = horizontalVelocity.normalized;
            Vector3 delayedRelease = aircraft.transform.position + aircraftVelocity * launchDelay;
            Vector3 predictedImpact = delayedRelease + horizontalVelocity * fallTime;
            Vector3 predictedTarget = target.transform.position + targetVelocity * (fallTime + launchDelay);
            Vector3 miss = predictedTarget - predictedImpact;
            miss.y = 0f;
            Vector3 relativeVelocity = horizontalVelocity - new Vector3(targetVelocity.x, 0f, targetVelocity.z);
            float closingSpeed = Mathf.Max(Vector3.Dot(relativeVelocity, direction), 1f);
            float correctedTimeError = Vector3.Dot(miss, direction) / closingSpeed;

            ReleasePass pass = GetPass(aircraft, target, station.Number);
            if (correctedTimeError > pass.BiasSeconds)
            {
                HorusLog.Trace("Bombing", "Gate:" + aircraft.GetInstanceID(),
                    $"Holding {aircraft.unitName} bomb: correctedError={correctedTimeError:F2}s bias={pass.BiasSeconds:F2}s delay={launchDelay:F2}s skill={aircraft.skill:F2}.", 0.5f);
                return false;
            }

            // If the native 1.5s window and a very unusual weapon topology disagree,
            // never strand the attack indefinitely: a late solution fails open.
            if (correctedTimeError < -1.5f)
                HorusLog.Once("Bombing", "LateRelease:" + aircraft.GetInstanceID(),
                    $"Late corrected release for {aircraft.unitName}; allowing native drop.", HorusLogLevel.Verbose);
            passes.Remove(aircraft);
            HorusLog.Verbose("Bombing",
                $"Corrected bomb release: aircraft={aircraft.unitName} target={target.unitName} error={correctedTimeError:F2}s bias={pass.BiasSeconds:F2}s delay={launchDelay:F2}s skill={aircraft.skill:F2}.");
            return true;
        }

        private static ReleasePass GetPass(Aircraft aircraft, Unit target, byte station)
        {
            if (passes.TryGetValue(aircraft, out ReleasePass pass) && pass.Target == target && pass.Station == station)
                return pass;

            float skill = Mathf.Clamp01(aircraft.skill);
            float maxBias = Mathf.Lerp(0.65f, 0.08f, skill);
            unchecked
            {
                uint seed = (uint)(aircraft.GetInstanceID() * 73856093) ^
                    (uint)(target.GetInstanceID() * 19349663) ^
                    (uint)(Mathf.FloorToInt(Time.timeSinceLevelLoad / 4f) * 83492791) ^ station;
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                float normalized = (seed & 0x00ffffff) / 8388607.5f - 1f;
                pass = new ReleasePass { Target = target, Station = station, BiasSeconds = normalized * maxBias };
            }
            passes[aircraft] = pass;
            return pass;
        }

        private static float GetLaunchDelay(WeaponStation station)
        {
            if (RailDelayField == null || RailLengthField == null || RailSpeedField == null || station?.Weapons == null)
                return 0f;
            float maximum = 0f;
            for (int i = 0; i < station.Weapons.Count; i++)
            {
                if (!(station.Weapons[i] is MountedMissile mounted) || !mounted.IsAttached()) continue;
                float delay = (float)RailDelayField.GetValue(mounted);
                float length = (float)RailLengthField.GetValue(mounted);
                float speed = (float)RailSpeedField.GetValue(mounted);
                if (speed > 0.01f) delay += length / speed;
                maximum = Mathf.Max(maximum, delay);
            }
            return maximum;
        }
    }
}
