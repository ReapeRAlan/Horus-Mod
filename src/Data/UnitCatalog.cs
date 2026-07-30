using System;
using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.Data
{
    public enum UnitKind
    {
        All = -1,
        Aircraft = 0,
        Ground = 1,
        Sea = 2,
        Building = 3,
        Scenery = 4
    }

    [Flags]
    public enum UnitRole
    {
        None = 0,
        AntiSurface = 1,
        AntiAir = 2,
        AntiMissile = 4,
        AntiRadar = 8,
        Radar = 16,
        Strategic = 32
    }

    public sealed class UnitEntry
    {
        public UnitDefinition Def;
        public string Key;
        public string Display;
        public string SearchKey;
        public float Cost;
        public Sprite Icon;
        public UnitRole Roles;
        public UnitKind Kind;
        public float MinAlt;
        public float MaxAlt;
    }

    public static class UnitCatalog
    {
        private static readonly List<UnitEntry> entries = new List<UnitEntry>();
        private static readonly Dictionary<string, UnitEntry> byKey = new Dictionary<string, UnitEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, IReadOnlyList<UnitEntry>> queryCache = new Dictionary<string, IReadOnlyList<UnitEntry>>();
        private static Encyclopedia builtFrom;

        public static IReadOnlyList<UnitEntry> Entries => entries;
        public static bool Built => builtFrom != null;

        public static void EnsureBuilt(bool includeEventContent = false)
        {
            if (Encyclopedia.i == null) return;
            if (builtFrom == Encyclopedia.i && entries.Count > 0) return;
            Build(includeEventContent);
        }

        public static void Build(bool includeEventContent = false)
        {
            entries.Clear();
            byKey.Clear();
            queryCache.Clear();
            builtFrom = Encyclopedia.i;
            if (builtFrom == null) return;

            AddDefinitions(builtFrom.aircraft, UnitKind.Aircraft, includeEventContent);
            AddDefinitions(builtFrom.vehicles, UnitKind.Ground, includeEventContent);
            AddDefinitions(builtFrom.ships, UnitKind.Sea, includeEventContent);
            AddDefinitions(builtFrom.buildings, UnitKind.Building, includeEventContent);
            AddDefinitions(builtFrom.scenery, UnitKind.Scenery, includeEventContent);
            entries.Sort((a, b) =>
            {
                int cost = b.Cost.CompareTo(a.Cost);
                return cost != 0 ? cost : string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void AddDefinitions<T>(IEnumerable<T> definitions, UnitKind kind, bool includeEventContent) where T : UnitDefinition
        {
            if (definitions == null) return;
            foreach (UnitDefinition def in definitions)
            {
                if (def == null || !def.IsAllowed(includeEventContent)) continue;
                if (string.IsNullOrWhiteSpace(def.unitName) || def.unitName == "???") continue;
                string key = !string.IsNullOrWhiteSpace(def.jsonKey) ? def.jsonKey : kind + ":" + def.name;
                if (byKey.ContainsKey(key)) continue;
                var entry = new UnitEntry
                {
                    Def = def,
                    Key = key,
                    Display = def.unitName,
                    SearchKey = Normalize($"{def.unitName} {def.code} {def.jsonKey} {def.description}"),
                    Cost = Mathf.Max(0f, def.value),
                    Icon = def.friendlyIcon != null ? def.friendlyIcon : def.mapIcon,
                    Roles = ResolveRoles(def),
                    Kind = kind,
                    MinAlt = def.minEditorHeight,
                    MaxAlt = Mathf.Max(def.minEditorHeight, def.maxEditorHeight)
                };
                entries.Add(entry);
                byKey.Add(key, entry);
            }
        }

        public static IReadOnlyList<UnitEntry> Query(UnitKind kind, UnitRole roles, string search, bool favoritesOnly)
        {
            EnsureBuilt();
            string needle = Normalize(search);
            string cacheKey = $"{(int)kind}|{(int)roles}|{needle}|{favoritesOnly}";
            if (queryCache.TryGetValue(cacheKey, out IReadOnlyList<UnitEntry> cached)) return cached;

            var exact = new List<UnitEntry>();
            foreach (UnitEntry entry in entries)
            {
                if (kind != UnitKind.All && entry.Kind != kind) continue;
                if (roles != UnitRole.None && (entry.Roles & roles) == 0) continue;
                if (favoritesOnly && !HorusPrefs.IsFavorite(entry.Key)) continue;
                if (needle.Length > 0 && entry.SearchKey.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                exact.Add(entry);
            }

            if (exact.Count == 0 && needle.Length > 0)
            {
                foreach (UnitEntry entry in entries)
                {
                    if (kind != UnitKind.All && entry.Kind != kind) continue;
                    if (roles != UnitRole.None && (entry.Roles & roles) == 0) continue;
                    if (favoritesOnly && !HorusPrefs.IsFavorite(entry.Key)) continue;
                    if (IsSubsequence(needle, entry.SearchKey)) exact.Add(entry);
                }
            }

            queryCache[cacheKey] = exact;
            return exact;
        }

        public static UnitEntry Find(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && byKey.TryGetValue(key, out UnitEntry entry) ? entry : null;
        }

        public static void InvalidateQueries() => queryCache.Clear();

        public static int Count(UnitKind kind)
        {
            EnsureBuilt();
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Kind == kind) count++;
            return count;
        }

        private static UnitRole ResolveRoles(UnitDefinition def)
        {
            UnitRole result = UnitRole.None;
            if (def.roleIdentity.antiSurface >= 0.5f) result |= UnitRole.AntiSurface;
            if (def.roleIdentity.antiAir >= 0.5f) result |= UnitRole.AntiAir;
            if (def.roleIdentity.antiMissile >= 0.5f) result |= UnitRole.AntiMissile;
            if (def.roleIdentity.antiRadar >= 0.5f) result |= UnitRole.AntiRadar;
            if (def.typeIdentity.radar >= 0.5f) result |= UnitRole.Radar;
            if (def.typeIdentity.strategic >= 0.5f) result |= UnitRole.Strategic;
            return result;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var chars = new char[value.Length];
            int length = 0;
            foreach (char c in value)
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)) chars[length++] = char.ToLowerInvariant(c);
            return new string(chars, 0, length);
        }

        private static bool IsSubsequence(string needle, string haystack)
        {
            int index = 0;
            for (int i = 0; i < haystack.Length && index < needle.Length; i++)
                if (haystack[i] == needle[index]) index++;
            return index == needle.Length;
        }
    }
}
