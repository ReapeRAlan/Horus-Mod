using System;

namespace HorusMod.Spawning
{
    /// <summary>
    /// Explicit live-ordnance placement modes. Targeted modes never engage merely because
    /// a unit happens to be selected; the operator must choose one in the ordnance panel.
    /// </summary>
    public enum HorusOrdnanceTargetMode
    {
        WorldPoint = 0,
        TrackSelected,
        ImpactSelected
    }

    /// <summary>Pure policy/math shared by the runtime and the standalone logic tests.</summary>
    public static class HorusOrdnanceTargetPolicy
    {
        public static bool RequiresSelectedUnit(HorusOrdnanceTargetMode mode)
        {
            return mode != HorusOrdnanceTargetMode.WorldPoint;
        }

        public static bool RequiresNativeSeeker(HorusOrdnanceTargetMode mode)
        {
            return mode == HorusOrdnanceTargetMode.TrackSelected;
        }

        public static bool UsesTargetRelativeSpawn(HorusOrdnanceTargetMode mode)
        {
            return mode == HorusOrdnanceTargetMode.ImpactSelected;
        }

        /// <summary>
        /// Estimates time to descend a vertical distance with an initial downward speed.
        /// This is used only to lead a moving target; the spawned weapon keeps native physics.
        /// </summary>
        public static double EstimateFallTime(double height, double initialDownSpeed, double gravity = 9.81d)
        {
            height = Math.Max(0d, height);
            initialDownSpeed = Math.Max(0d, initialDownSpeed);
            gravity = Math.Max(0.0001d, gravity);
            if (height <= 0d) return 0d;
            return (-initialDownSpeed + Math.Sqrt(initialDownSpeed * initialDownSpeed + 2d * gravity * height)) / gravity;
        }
    }
}
