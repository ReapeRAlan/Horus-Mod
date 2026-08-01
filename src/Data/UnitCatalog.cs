using System;
using System.Collections.Generic;
using System.Reflection;
using HorusMod.Logging;
using UnityEngine;

namespace HorusMod.Data
{
    // Existing values are intentionally stable for config/UI compatibility.
    public enum UnitKind
    {
        All = -1,
        Aircraft = 0,
        Ground = 1,
        Sea = 2,
        Building = 3,
        Scenery = 4,
        Missile = 5,
        Other = 6
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

    public static class UnitCatalog
    {
        private sealed class Candidate
        {
            public UnitDefinition Definition;
            public string Source;
        }

        private static readonly List<UnitEntry> entries = new List<UnitEntry>();
        private static readonly Dictionary<string, UnitEntry> byKey = new Dictionary<string, UnitEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<UnitEntry>> byDefinitionKey = new Dictionary<string, List<UnitEntry>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, IReadOnlyList<UnitEntry>> queryCache = new Dictionary<string, IReadOnlyList<UnitEntry>>();
        private static readonly List<CatalogKeyConflict> conflicts = new List<CatalogKeyConflict>();
        private static Encyclopedia builtFrom;
        private static ulong builtFingerprint;
        private static bool built;
        private static bool lastIncludeEventContent;
        private static int lastFingerprintFrame = -1;
        private static Encyclopedia lastObservedEncyclopedia;
        private static bool lastObservedIncludeEventContent;
        private static ulong lastObservedFingerprint;
        private static float nextAutomaticFingerprintTime;
        private const float AutomaticFingerprintInterval = 0.75f;

        private static readonly FieldInfo disabledField = typeof(UnitDefinition).GetField("disabled", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo eventField = typeof(UnitDefinition).GetField("isEventContent", BindingFlags.Instance | BindingFlags.NonPublic);

        public static IReadOnlyList<UnitEntry> Entries => entries;
        public static IReadOnlyList<CatalogEntry> CatalogEntries => entries;
        public static IReadOnlyList<CatalogKeyConflict> Conflicts => conflicts;
        public static bool Built => built && builtFrom != null;
        public static int Revision { get; private set; }
        public static ulong Fingerprint => builtFingerprint;

        /// <summary>
        /// Rebuilds automatically when definitions, lookup/index registration,
        /// labels, prefabs or availability change. This catches content injected
        /// by mods after Horus first opens.
        /// </summary>
        public static void EnsureBuilt(bool includeEventContent = false)
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null) return;

            if (built && ReferenceEquals(builtFrom, encyclopedia) &&
                lastIncludeEventContent == includeEventContent &&
                Time.unscaledTime < nextAutomaticFingerprintTime)
                return;

            ulong currentFingerprint;
            if (lastFingerprintFrame == Time.frameCount && ReferenceEquals(lastObservedEncyclopedia, encyclopedia) &&
                lastObservedIncludeEventContent == includeEventContent)
            {
                currentFingerprint = lastObservedFingerprint;
            }
            else
            {
                try
                {
                    currentFingerprint = CalculateFingerprint(encyclopedia, includeEventContent);
                    lastFingerprintFrame = Time.frameCount;
                    lastObservedEncyclopedia = encyclopedia;
                    lastObservedIncludeEventContent = includeEventContent;
                    lastObservedFingerprint = currentFingerprint;
                }
                catch (Exception ex)
                {
                    HorusLog.Warning("Catalog", "Unable to fingerprint definitions; forcing refresh: " + ex.GetType().Name);
                    Build(includeEventContent);
                    return;
                }
            }

            if (built && ReferenceEquals(builtFrom, encyclopedia) && builtFingerprint == currentFingerprint &&
                lastIncludeEventContent == includeEventContent)
            {
                nextAutomaticFingerprintTime = Time.unscaledTime + AutomaticFingerprintInterval;
                return;
            }

            Build(includeEventContent);
        }

        /// <summary>Explicit refresh entry point for UI and diagnostics.</summary>
        public static void Refresh(bool includeEventContent = false)
        {
            Build(includeEventContent);
        }

        public static void Invalidate()
        {
            built = false;
            builtFrom = null;
            builtFingerprint = 0UL;
            lastFingerprintFrame = -1;
            lastObservedEncyclopedia = null;
            nextAutomaticFingerprintTime = 0f;
            queryCache.Clear();
        }

        public static void Build(bool includeEventContent = false)
        {
            entries.Clear();
            byKey.Clear();
            byDefinitionKey.Clear();
            queryCache.Clear();
            conflicts.Clear();

            Encyclopedia encyclopedia = Encyclopedia.i;
            builtFrom = encyclopedia;
            lastIncludeEventContent = includeEventContent;
            built = encyclopedia != null;
            if (encyclopedia == null)
            {
                builtFingerprint = 0UL;
                return;
            }

            var registeredIds = new HashSet<int>();
            if (encyclopedia.IndexLookup != null)
            {
                for (int i = 0; i < encyclopedia.IndexLookup.Count; i++)
                {
                    if (encyclopedia.IndexLookup[i] is UnitDefinition registered && registered != null)
                        registeredIds.Add(registered.GetInstanceID());
                }
            }

            var candidates = new List<Candidate>();
            var seenDefinitions = new HashSet<int>();
            AddDefinitions(candidates, seenDefinitions, encyclopedia.aircraft, "Aircraft list");
            AddDefinitions(candidates, seenDefinitions, encyclopedia.vehicles, "Vehicle list");
            AddDefinitions(candidates, seenDefinitions, encyclopedia.ships, "Ship list");
            AddDefinitions(candidates, seenDefinitions, encyclopedia.buildings, "Building list");
            AddDefinitions(candidates, seenDefinitions, encyclopedia.scenery, "Scenery list");
            AddDefinitions(candidates, seenDefinitions, encyclopedia.missiles, "Missile list");
            AddDefinitions(candidates, seenDefinitions, encyclopedia.otherUnits, "Other-unit list");

            // Mods sometimes register definitions directly in Lookup without
            // extending the typed lists or network index. Keep those visible.
            if (Encyclopedia.Lookup != null)
            {
                foreach (KeyValuePair<string, UnitDefinition> pair in Encyclopedia.Lookup)
                {
                    UnitDefinition definition = pair.Value;
                    if (definition == null || !seenDefinitions.Add(definition.GetInstanceID())) continue;
                    candidates.Add(new Candidate
                    {
                        Definition = definition,
                        Source = "Lookup[" + (pair.Key ?? "") + "]"
                    });
                }
            }

            var keyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Count; i++)
            {
                string jsonKey = candidates[i].Definition.jsonKey;
                if (string.IsNullOrWhiteSpace(jsonKey)) continue;
                keyCounts.TryGetValue(jsonKey, out int count);
                keyCounts[jsonKey] = count + 1;
            }

            var keyOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                UnitDefinition definition = candidate.Definition;
                string jsonKey = definition.jsonKey ?? "";
                bool duplicate = !string.IsNullOrWhiteSpace(jsonKey) && keyCounts.TryGetValue(jsonKey, out int count) && count > 1;
                int ordinal = 1;
                if (duplicate)
                {
                    keyOrdinals.TryGetValue(jsonKey, out int previous);
                    ordinal = previous + 1;
                    keyOrdinals[jsonKey] = ordinal;
                }

                UnitEntry entry = CreateEntry(definition, candidate.Source, registeredIds.Contains(definition.GetInstanceID()),
                    includeEventContent, duplicate, ordinal);
                entries.Add(entry);
                byKey[entry.Key] = entry;

                if (!string.IsNullOrWhiteSpace(jsonKey))
                {
                    if (!byDefinitionKey.TryGetValue(jsonKey, out List<UnitEntry> matching))
                    {
                        matching = new List<UnitEntry>();
                        byDefinitionKey.Add(jsonKey, matching);
                    }
                    matching.Add(entry);
                }
            }

            foreach (KeyValuePair<string, List<UnitEntry>> pair in byDefinitionKey)
            {
                if (pair.Value.Count < 2) continue;
                conflicts.Add(new CatalogKeyConflict(pair.Key, pair.Value));
                HorusLog.Warning("Catalog", $"Duplicate jsonKey '{pair.Key}' found in {pair.Value.Count} definitions; all entries remain visible.");
            }

            entries.Sort((a, b) =>
            {
                int cost = b.Cost.CompareTo(a.Cost);
                if (cost != 0) return cost;
                int display = string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
                return display != 0 ? display : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });

            try
            {
                builtFingerprint = CalculateFingerprint(encyclopedia, includeEventContent);
                lastFingerprintFrame = Time.frameCount;
                lastObservedEncyclopedia = encyclopedia;
                lastObservedIncludeEventContent = includeEventContent;
                lastObservedFingerprint = builtFingerprint;
                nextAutomaticFingerprintTime = Time.unscaledTime + AutomaticFingerprintInterval;
            }
            catch
            {
                // A concurrently changing mod list will cause one more rebuild
                // on the next EnsureBuilt call rather than leaving stale data.
                builtFingerprint = 0UL;
                lastFingerprintFrame = -1;
            }

            Revision++;
            HorusLog.Verbose("Catalog", $"Catalog revision {Revision}: {entries.Count} definitions, {conflicts.Count} key conflicts.");
        }

        private static UnitEntry CreateEntry(UnitDefinition definition, string source, bool isRegistered,
            bool includeEventContent, bool duplicate, int duplicateOrdinal)
        {
            ResolveDefinitionFlags(definition, out bool disabled, out bool eventContent);
            bool unlabeled = string.IsNullOrWhiteSpace(definition.unitName) ||
                             string.Equals(definition.unitName.Trim(), "???", StringComparison.Ordinal);
            SpawnKind spawnKind = ResolveSpawnKind(definition);
            UnitKind unitKind = ResolveUnitKind(spawnKind);
            NetworkRegistration registration = isRegistered
                ? NetworkRegistration.NetworkRegistered
                : NetworkRegistration.LookupOnly;
            SupplyCapabilities supply = SupplyCapabilities.Inspect(definition);
            PlacementSurface surface = ResolvePlacementSurface(spawnKind, supply);
            UnitRole roles = ResolveRoles(definition);

            CatalogFlags flags = supply.Flags;
            if (disabled) flags |= CatalogFlags.Disabled;
            if (eventContent) flags |= CatalogFlags.Event;
            if (unlabeled) flags |= CatalogFlags.Unlabeled;
            if (!isRegistered) flags |= CatalogFlags.LookupOnly;
            if (IsModdedDefinition(definition)) flags |= CatalogFlags.Modded;
            if (duplicate) flags |= CatalogFlags.DuplicateJsonKey;
            if (spawnKind == SpawnKind.Missile) flags |= CatalogFlags.LiveOrdnance;
            ResolveOrdnanceFlags(definition, spawnKind, out bool nuclear, out bool strategic);
            if (nuclear) flags |= CatalogFlags.Nuclear;
            if (strategic || (roles & UnitRole.Strategic) != 0) flags |= CatalogFlags.Strategic;
            if (disabled || eventContent || unlabeled || !isRegistered || spawnKind == SpawnKind.Missile)
                flags |= CatalogFlags.Experimental;

            string jsonKey = definition.jsonKey ?? "";
            string identity = !string.IsNullOrWhiteSpace(jsonKey)
                ? jsonKey.Trim()
                : (!string.IsNullOrWhiteSpace(definition.name) ? definition.name.Trim() : "instance-" + definition.GetInstanceID());
            string catalogKey = !string.IsNullOrWhiteSpace(jsonKey)
                ? jsonKey.Trim()
                : "@" + spawnKind + ":" + identity + ":" + definition.GetInstanceID();
            if (duplicate && duplicateOrdinal > 1) catalogKey += "#duplicate-" + duplicateOrdinal;

            string display = unlabeled ? "??? · " + identity : definition.unitName.Trim();
            string flagTerms = flags.ToString().Replace(",", " ");
            return new UnitEntry
            {
                Def = definition,
                Key = catalogKey,
                JsonKey = jsonKey,
                Display = display,
                SearchKey = Normalize($"{display} {definition.unitName} {definition.code} {jsonKey} {definition.description} {source} {spawnKind} {surface} {flagTerms}"),
                Cost = Mathf.Max(0f, definition.value),
                Icon = definition.friendlyIcon != null ? definition.friendlyIcon : definition.mapIcon,
                Roles = roles,
                Kind = unitKind,
                MinAlt = definition.minEditorHeight,
                MaxAlt = Mathf.Max(definition.minEditorHeight, definition.maxEditorHeight),
                Source = source,
                SpawnKind = spawnKind,
                PlacementSurface = surface,
                Registration = registration,
                Flags = flags,
                Supply = supply,
                AllowedInCurrentMode = !disabled && (!eventContent || includeEventContent)
            };
        }

        private static void AddDefinitions<T>(List<Candidate> destination, HashSet<int> seen, IEnumerable<T> definitions, string source)
            where T : UnitDefinition
        {
            if (definitions == null) return;
            foreach (T definition in definitions)
            {
                if (definition == null || !seen.Add(definition.GetInstanceID())) continue;
                destination.Add(new Candidate { Definition = definition, Source = source });
            }
        }

        public static IReadOnlyList<UnitEntry> Query(UnitKind kind, UnitRole roles, string search, bool favoritesOnly)
        {
            return Query(kind, roles, CatalogFlags.None, search, favoritesOnly);
        }

        /// <summary>
        /// Capability-aware query. Multiple required flags use OR semantics,
        /// matching the existing role-chip behavior.
        /// </summary>
        public static IReadOnlyList<UnitEntry> Query(UnitKind kind, UnitRole roles, CatalogFlags requiredFlags,
            string search, bool favoritesOnly)
        {
            EnsureBuilt(lastIncludeEventContent);
            string needle = Normalize(search);
            string cacheKey = $"{(int)kind}|{(int)roles}|{(int)requiredFlags}|{needle}|{favoritesOnly}";
            if (queryCache.TryGetValue(cacheKey, out IReadOnlyList<UnitEntry> cached)) return cached;

            var exact = new List<UnitEntry>();
            foreach (UnitEntry entry in entries)
            {
                if (kind != UnitKind.All && entry.Kind != kind) continue;
                if (roles != UnitRole.None && (entry.Roles & roles) == 0) continue;
                if (requiredFlags != CatalogFlags.None && (entry.Flags & requiredFlags) == 0) continue;
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
                    if (requiredFlags != CatalogFlags.None && (entry.Flags & requiredFlags) == 0) continue;
                    if (favoritesOnly && !HorusPrefs.IsFavorite(entry.Key)) continue;
                    if (IsSubsequence(needle, entry.SearchKey)) exact.Add(entry);
                }
            }

            queryCache[cacheKey] = exact;
            return exact;
        }

        public static UnitEntry Find(string key)
        {
            EnsureBuilt(lastIncludeEventContent);
            if (string.IsNullOrEmpty(key)) return null;
            if (byKey.TryGetValue(key, out UnitEntry exact)) return exact;
            return byDefinitionKey.TryGetValue(key, out List<UnitEntry> matching) && matching.Count > 0 ? matching[0] : null;
        }

        public static IReadOnlyList<UnitEntry> FindAll(string jsonKey)
        {
            EnsureBuilt(lastIncludeEventContent);
            return !string.IsNullOrEmpty(jsonKey) && byDefinitionKey.TryGetValue(jsonKey, out List<UnitEntry> matching)
                ? matching
                : Array.Empty<UnitEntry>();
        }

        public static UnitEntry FindByDefinition(UnitDefinition definition)
        {
            EnsureBuilt(lastIncludeEventContent);
            if (definition == null) return null;
            if (!string.IsNullOrWhiteSpace(definition.jsonKey) &&
                byDefinitionKey.TryGetValue(definition.jsonKey, out List<UnitEntry> matching))
            {
                for (int i = 0; i < matching.Count; i++)
                    if (ReferenceEquals(matching[i].Def, definition)) return matching[i];
            }
            for (int i = 0; i < entries.Count; i++)
                if (ReferenceEquals(entries[i].Def, definition)) return entries[i];
            return null;
        }

        public static void InvalidateQueries() => queryCache.Clear();

        public static int Count(UnitKind kind)
        {
            EnsureBuilt(lastIncludeEventContent);
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
                if (kind == UnitKind.All || entries[i].Kind == kind) count++;
            return count;
        }

        private static SpawnKind ResolveSpawnKind(UnitDefinition definition)
        {
            if (definition is AircraftDefinition) return SpawnKind.Aircraft;
            if (definition is VehicleDefinition) return SpawnKind.Vehicle;
            if (definition is ShipDefinition) return SpawnKind.Ship;
            if (definition is BuildingDefinition) return SpawnKind.Building;
            if (definition is SceneryDefinition) return SpawnKind.Scenery;
            if (definition is MissileDefinition) return SpawnKind.Missile;
            // Lookup-only mod definitions are occasionally exposed through a base
            // UnitDefinition. Prefer their concrete prefab component over labels.
            if (PrefabHasComponent(definition.unitPrefab, "Aircraft")) return SpawnKind.Aircraft;
            if (PrefabHasComponent(definition.unitPrefab, "GroundVehicle")) return SpawnKind.Vehicle;
            if (PrefabHasComponent(definition.unitPrefab, "Ship")) return SpawnKind.Ship;
            if (PrefabHasComponent(definition.unitPrefab, "Building")) return SpawnKind.Building;
            if (PrefabHasComponent(definition.unitPrefab, "Scenery")) return SpawnKind.Scenery;
            if (PrefabHasComponent(definition.unitPrefab, "Missile")) return SpawnKind.Missile;
            return PrefabHasComponent(definition.unitPrefab, "Container") ? SpawnKind.Container : SpawnKind.Other;
        }

        private static UnitKind ResolveUnitKind(SpawnKind spawnKind)
        {
            switch (spawnKind)
            {
                case SpawnKind.Aircraft: return UnitKind.Aircraft;
                case SpawnKind.Vehicle: return UnitKind.Ground;
                case SpawnKind.Ship: return UnitKind.Sea;
                case SpawnKind.Building: return UnitKind.Building;
                case SpawnKind.Scenery: return UnitKind.Scenery;
                case SpawnKind.Missile: return UnitKind.Missile;
                default: return UnitKind.Other;
            }
        }

        private static PlacementSurface ResolvePlacementSurface(SpawnKind spawnKind, SupplyCapabilities supply)
        {
            switch (spawnKind)
            {
                case SpawnKind.Aircraft: return PlacementSurface.Air;
                case SpawnKind.Ship: return PlacementSurface.Sea;
                case SpawnKind.Vehicle:
                case SpawnKind.Building:
                case SpawnKind.Scenery:
                    return PlacementSurface.Ground;
                case SpawnKind.Container:
                    // Naval Rearmer containers float at sea but must still use the
                    // Container spawn route. Other logistics containers are ground
                    // objects; decorative/unknown containers remain freely placeable.
                    if (supply != null && supply.CanResupplyShips == CapabilityState.Yes)
                        return PlacementSurface.Sea;
                    if (supply != null && supply.IsLogistics)
                        return PlacementSurface.Ground;
                    return PlacementSurface.Free;
                default:
                    return PlacementSurface.Free;
            }
        }

        private static bool PrefabHasComponent(GameObject prefab, string componentName)
        {
            if (prefab == null) return false;
            try
            {
                // Native Spawner methods require the concrete Unit component on the
                // prefab root. Classifying a child component would advertise a route
                // that cannot actually be spawned safely.
                Component[] components = prefab.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    for (Type type = component != null ? component.GetType() : null; type != null; type = type.BaseType)
                        if (string.Equals(type.Name, componentName, StringComparison.Ordinal)) return true;
                }
            }
            catch
            {
                // An unreadable prefab remains Other rather than being guessed.
            }
            return false;
        }

        private static bool IsModdedDefinition(UnitDefinition definition)
        {
            if (definition == null) return false;
            try
            {
                // This is intentionally conservative. Asset-only mods are not always
                // distinguishable at runtime, but built-in hidden definitions must not
                // be mislabeled merely because they are Lookup-only.
                return definition.GetType().Assembly != typeof(UnitDefinition).Assembly;
            }
            catch
            {
                return false;
            }
        }

        private static void ResolveOrdnanceFlags(UnitDefinition definition, SpawnKind spawnKind,
            out bool nuclear, out bool strategic)
        {
            nuclear = false;
            strategic = false;
            if (definition == null || spawnKind != SpawnKind.Missile || definition.unitPrefab == null) return;
            try
            {
                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                WeaponInfo info = missile != null ? missile.GetWeaponInfo() : null;
                nuclear = info != null && info.nuclear;
                strategic = info != null && info.strategic;
            }
            catch
            {
                // typeIdentity remains the conservative strategic fallback.
            }
        }

        private static void ResolveDefinitionFlags(UnitDefinition definition, out bool disabled, out bool eventContent)
        {
            bool allowedNormally = SafeIsAllowed(definition, false);
            bool allowedWithEvents = SafeIsAllowed(definition, true);
            disabled = !allowedWithEvents;
            eventContent = allowedWithEvents && !allowedNormally;

            try
            {
                if (disabledField != null && disabledField.GetValue(definition) is bool disabledValue)
                    disabled = disabledValue;
                if (eventField != null && eventField.GetValue(definition) is bool eventValue)
                    eventContent = eventValue;
            }
            catch
            {
                // Public IsAllowed behavior above remains a safe fallback.
            }
        }

        private static bool SafeIsAllowed(UnitDefinition definition, bool includeEventContent)
        {
            try { return definition != null && definition.IsAllowed(includeEventContent); }
            catch { return false; }
        }

        private static UnitRole ResolveRoles(UnitDefinition definition)
        {
            UnitRole result = UnitRole.None;
            if (definition.roleIdentity.antiSurface >= 0.5f) result |= UnitRole.AntiSurface;
            if (definition.roleIdentity.antiAir >= 0.5f) result |= UnitRole.AntiAir;
            if (definition.roleIdentity.antiMissile >= 0.5f) result |= UnitRole.AntiMissile;
            if (definition.roleIdentity.antiRadar >= 0.5f) result |= UnitRole.AntiRadar;
            if (definition.typeIdentity.radar >= 0.5f) result |= UnitRole.Radar;
            if (definition.typeIdentity.strategic >= 0.5f) result |= UnitRole.Strategic;
            return result;
        }

        private static ulong CalculateFingerprint(Encyclopedia encyclopedia, bool includeEventContent)
        {
            ulong hash = 1469598103934665603UL;
            Mix(ref hash, encyclopedia != null ? encyclopedia.GetInstanceID() : 0);
            Mix(ref hash, includeEventContent ? 1 : 0);
            HashDefinitions(ref hash, encyclopedia.aircraft, 101);
            HashDefinitions(ref hash, encyclopedia.vehicles, 103);
            HashDefinitions(ref hash, encyclopedia.ships, 107);
            HashDefinitions(ref hash, encyclopedia.buildings, 109);
            HashDefinitions(ref hash, encyclopedia.scenery, 113);
            HashDefinitions(ref hash, encyclopedia.missiles, 127);
            HashDefinitions(ref hash, encyclopedia.otherUnits, 131);

            Mix(ref hash, 137);
            if (Encyclopedia.Lookup == null)
            {
                Mix(ref hash, -1);
            }
            else
            {
                Mix(ref hash, Encyclopedia.Lookup.Count);
                foreach (KeyValuePair<string, UnitDefinition> pair in Encyclopedia.Lookup)
                {
                    Mix(ref hash, pair.Key);
                    HashDefinition(ref hash, pair.Value);
                }
            }

            Mix(ref hash, 139);
            if (encyclopedia.IndexLookup == null)
            {
                Mix(ref hash, -1);
            }
            else
            {
                Mix(ref hash, encyclopedia.IndexLookup.Count);
                for (int i = 0; i < encyclopedia.IndexLookup.Count; i++)
                {
                    object item = encyclopedia.IndexLookup[i];
                    if (item is UnityEngine.Object unityObject)
                        Mix(ref hash, unityObject != null ? unityObject.GetInstanceID() : 0);
                    else
                        Mix(ref hash, item != null ? item.GetType().FullName : "null");
                }
            }

            return hash;
        }

        private static void HashDefinitions<T>(ref ulong hash, IList<T> definitions, int marker) where T : UnitDefinition
        {
            Mix(ref hash, marker);
            if (definitions == null)
            {
                Mix(ref hash, -1);
                return;
            }

            Mix(ref hash, definitions.Count);
            for (int i = 0; i < definitions.Count; i++) HashDefinition(ref hash, definitions[i]);
        }

        private static void HashDefinition(ref ulong hash, UnitDefinition definition)
        {
            if (definition == null)
            {
                Mix(ref hash, 0);
                return;
            }

            Mix(ref hash, definition.GetInstanceID());
            Mix(ref hash, definition.jsonKey);
            Mix(ref hash, definition.unitName);
            Mix(ref hash, definition.unitPrefab != null ? definition.unitPrefab.GetInstanceID() : 0);
            Mix(ref hash, SafeIsAllowed(definition, false) ? 1 : 0);
            Mix(ref hash, SafeIsAllowed(definition, true) ? 1 : 0);
        }

        private static void Mix(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
            }
        }

        private static void Mix(ref ulong hash, string value)
        {
            unchecked
            {
                if (value == null)
                {
                    Mix(ref hash, -1);
                    return;
                }

                Mix(ref hash, value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 1099511628211UL;
                }
            }
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
