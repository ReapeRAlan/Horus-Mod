using System;
using System.Collections.Generic;
using System.Reflection;
using HorusMod.Logging;

namespace HorusMod.Compat
{
    /// <summary>
    /// One-time compatibility audit for game APIs used by Horus. Features still
    /// retain their local guards; this produces a precise update signal after a
    /// Nuclear Option patch instead of failing silently.
    /// </summary>
    public static class GameApi
    {
        private static readonly Dictionary<string, bool> status = new Dictionary<string, bool>();
        private static bool initialized;

        public static IReadOnlyDictionary<string, bool> Status => status;
        public static bool Ready => initialized && !status.ContainsValue(false);

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            CheckMember(typeof(UnitDefinition), "value");
            CheckMember(typeof(UnitDefinition), "friendlyIcon");
            CheckMember(typeof(UnitDefinition), "minEditorHeight");
            CheckMember(typeof(UnitDefinition), "maxEditorHeight");
            CheckMethod(typeof(UnitDefinition), "IsAllowed");
            CheckMethod(typeof(Faction), "GetConvoyGroups");
            CheckMethod(typeof(Datum), "WaterPlane");
            CheckMember(typeof(Aircraft), "Networkloadout");
            CheckMethod(typeof(DynamicMap), "SelectIcon");
            CheckMethod(typeof(DynamicMap), "DeselectAllIcons");
        }

        private static void CheckMethod(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            bool found = false;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.Name != name) continue;
                found = true;
                break;
            }
            Record(type.Name + "." + name, found);
        }

        private static void CheckMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            Record(type.Name + "." + name, type.GetField(name, flags) != null || type.GetProperty(name, flags) != null);
        }

        private static void Record(string symbol, bool present)
        {
            status[symbol] = present;
            if (!present) HorusLog.Warning("Compat", $"MISSING {symbol}");
        }
    }
}
