using System.Diagnostics;
using HorusMod.Logging;

namespace HorusMod.Diagnostics
{
    public static class HorusPerformanceTracker
    {
        private static readonly Stopwatch frameStopwatch = new Stopwatch();
        private static readonly long[] gcSamples = new long[60];
        private static int gcSampleIndex;
        private static int gcSampleCount;

        public static float LastTickCostMs { get; private set; }
        public static float AverageTickCostMs { get; private set; }
        public static int ActiveSpawnedUnitsCount { get; set; }
        public static int ActiveFactoriesCount { get; set; }
        public static float LastCleanupDurationMs { get; set; }
        public static long GcDelta60Frames { get; private set; }

        public static void BeginFrameTrace()
        {
            frameStopwatch.Restart();
        }

        public static void EndFrameTrace()
        {
            frameStopwatch.Stop();
            LastTickCostMs = (float)frameStopwatch.Elapsed.TotalMilliseconds;
            AverageTickCostMs = (AverageTickCostMs * 0.9f) + (LastTickCostMs * 0.1f);
            long current = System.GC.GetTotalMemory(false);
            if (gcSampleCount == gcSamples.Length)
                GcDelta60Frames = current - gcSamples[gcSampleIndex];
            else
                gcSampleCount++;
            gcSamples[gcSampleIndex] = current;
            gcSampleIndex = (gcSampleIndex + 1) % gcSamples.Length;
        }

        public static string GetDiagnosticSummary()
        {
            return $"Tick Cost: {LastTickCostMs:F2}ms (Avg: {AverageTickCostMs:F2}ms)\n" +
                   $"Active Spawned Units: {ActiveSpawnedUnitsCount}\n" +
                   $"Active Virtual Factories: {ActiveFactoriesCount}\n" +
                   $"Last Cleanup Duration: {LastCleanupDurationMs:F2}ms\n" +
                   $"GC Delta (60 frames): {GcDelta60Frames / 1024f:F1} KiB\n" +
                   $"Log Throttling Stats: Suppressed {HorusLog.SuppressedCount} logs ({HorusLog.ThrottleKeyCount} throttle keys)";
        }
    }
}
