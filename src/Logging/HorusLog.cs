using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace HorusMod.Logging
{
    public enum HorusLogLevel
    {
        Quiet = 0,
        Normal = 1,
        Verbose = 2,
        Trace = 3
    }

    /// <summary>
    /// Central, allocation-light logger with a stable subsystem prefix and throttled tracing.
    /// Warnings and errors are always emitted; informational output follows LogVerbosity.
    /// </summary>
    public static class HorusLog
    {
        private static readonly HashSet<string> onceKeys = new HashSet<string>();
        private static readonly Dictionary<string, float> lastTraceTime = new Dictionary<string, float>();

        public static int SuppressedCount { get; private set; }
        public static int ThrottleKeyCount => lastTraceTime.Count;

        private static HorusLogLevel Level =>
            HorusPlugin.LogVerbosity != null ? HorusPlugin.LogVerbosity.Value : HorusLogLevel.Normal;

        public static void Info(string subsystem, string message)
        {
            if (Level < HorusLogLevel.Normal) return;
            Write(LogLevel.Info, subsystem, message);
        }

        public static void Verbose(string subsystem, string message)
        {
            if (Level < HorusLogLevel.Verbose) return;
            Write(LogLevel.Info, subsystem, message);
        }

        public static void Trace(string subsystem, string key, string message, float minGapSeconds = 1f)
        {
            if (Level < HorusLogLevel.Trace) return;
            float now = Time.realtimeSinceStartup;
            string scopedKey = subsystem + ":" + key;
            if (lastTraceTime.TryGetValue(scopedKey, out float last) && now - last < minGapSeconds)
            {
                SuppressedCount++;
                return;
            }

            lastTraceTime[scopedKey] = now;
            Write(LogLevel.Info, subsystem, message);
        }

        public static void Once(string subsystem, string key, string message, HorusLogLevel minimum = HorusLogLevel.Normal)
        {
            if (Level < minimum) return;
            string scopedKey = subsystem + ":" + key;
            if (!onceKeys.Add(scopedKey))
            {
                SuppressedCount++;
                return;
            }

            Write(LogLevel.Info, subsystem, message);
        }

        public static void Warning(string subsystem, string message)
        {
            Write(LogLevel.Warning, subsystem, message);
        }

        public static void Error(string subsystem, string message)
        {
            Write(LogLevel.Error, subsystem, message);
        }

        private static void Write(LogLevel level, string subsystem, string message)
        {
            string formatted = $"[Horus:{subsystem}] {message}";
            if (HorusPlugin.Logger == null)
            {
                if (level == LogLevel.Error) Debug.LogError(formatted);
                else if (level == LogLevel.Warning) Debug.LogWarning(formatted);
                else Debug.Log(formatted);
                return;
            }

            if (level == LogLevel.Error) HorusPlugin.Logger.LogError(formatted);
            else if (level == LogLevel.Warning) HorusPlugin.Logger.LogWarning(formatted);
            else HorusPlugin.Logger.LogInfo(formatted);
        }

        public static void Reset()
        {
            onceKeys.Clear();
            lastTraceTime.Clear();
            SuppressedCount = 0;
        }
    }
}
