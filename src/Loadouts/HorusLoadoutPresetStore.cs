using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HorusMod.Logging;
using UnityEngine;

namespace HorusMod.Loadouts
{
    [Serializable]
    public sealed class HorusLoadoutPreset
    {
        public string aircraftJsonKey;
        public string presetId;
        public string name;
        public List<string> weaponMountJsonKeys = new List<string>();

        public string AircraftJsonKey => aircraftJsonKey ?? "";
        public string PresetId => presetId ?? "";
        public string Name => name ?? "";
        public IReadOnlyList<string> WeaponMountJsonKeys => weaponMountJsonKeys;

        public HorusLoadoutPreset Clone()
        {
            return new HorusLoadoutPreset
            {
                aircraftJsonKey = aircraftJsonKey ?? "",
                presetId = presetId ?? "",
                name = name ?? "",
                weaponMountJsonKeys = weaponMountJsonKeys != null
                    ? new List<string>(weaponMountJsonKeys)
                    : new List<string>()
            };
        }
    }

    [Serializable]
    internal sealed class HorusLoadoutPresetDocument
    {
        // Keep the CLR default (0) so a legacy file with no schemaVersion is
        // distinguishable from a newly-created v1 document.
        public int schemaVersion;
        public List<HorusLoadoutPreset> presets = new List<HorusLoadoutPreset>();
    }

    /// <summary>
    /// Versioned, defensive persistence for named Horus loadouts. The store keeps
    /// one unique (case-insensitive) name per aircraft and writes atomically where
    /// the platform supports File.Replace.
    /// </summary>
    public static class HorusLoadoutPresetStore
    {
        public const int CurrentSchemaVersion = 1;

        private const string Subsystem = "LoadoutStore";
        private const string FileName = "aircraft_loadouts.json";
        private static readonly object Gate = new object();

        private static HorusLoadoutPresetDocument document;
        private static bool loaded;
        private static bool readOnly;
        private static bool needsMigrationSave;
        private static bool invalidFileNeedsBackup;
        private static string lastLoadError;

        public static string ConfigDirectory => System.IO.Path.Combine(Paths.ConfigPath, "HorusMod");
        public static string FilePath => System.IO.Path.Combine(ConfigDirectory, FileName);
        public static bool IsLoaded { get { lock (Gate) return loaded; } }
        public static bool IsReadOnly { get { lock (Gate) return readOnly; } }
        public static bool NeedsMigrationSave { get { lock (Gate) return needsMigrationSave; } }
        public static string LastLoadError { get { lock (Gate) return lastLoadError; } }

        public static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (loaded) return;
                LoadLocked();
            }
        }

        public static void Reload()
        {
            lock (Gate)
            {
                loaded = false;
                readOnly = false;
                needsMigrationSave = false;
                invalidFileNeedsBackup = false;
                lastLoadError = null;
                document = null;
                LoadLocked();
            }
        }

        public static IReadOnlyList<HorusLoadoutPreset> GetPresets(string aircraftJsonKey)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                var result = new List<HorusLoadoutPreset>();
                string key = Normalize(aircraftJsonKey);
                if (string.IsNullOrEmpty(key)) return result;

                for (int i = 0; i < document.presets.Count; i++)
                {
                    HorusLoadoutPreset preset = document.presets[i];
                    if (preset != null && Same(preset.aircraftJsonKey, key)) result.Add(preset.Clone());
                }
                result.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
                return result;
            }
        }

        public static IReadOnlyList<HorusLoadoutPreset> GetAllPresets()
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                var result = new List<HorusLoadoutPreset>(document.presets.Count);
                for (int i = 0; i < document.presets.Count; i++)
                    if (document.presets[i] != null) result.Add(document.presets[i].Clone());
                return result;
            }
        }

        public static bool TryGetPreset(string presetId, out HorusLoadoutPreset preset)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                HorusLoadoutPreset found = FindByIdLocked(presetId);
                preset = found?.Clone();
                return found != null;
            }
        }

        public static bool Create(
            string aircraftJsonKey,
            string name,
            IEnumerable<string> weaponMountJsonKeys,
            out HorusLoadoutPreset preset,
            out string error)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                preset = null;
                if (!CanWriteLocked(out error)) return false;
                if (!ValidateIdentityLocked(aircraftJsonKey, name, null, out string key, out string cleanName, out error)) return false;

                var created = new HorusLoadoutPreset
                {
                    aircraftJsonKey = key,
                    presetId = Guid.NewGuid().ToString("N"),
                    name = cleanName,
                    weaponMountJsonKeys = CopyKeys(weaponMountJsonKeys)
                };
                document.presets.Add(created);
                if (!TrySaveLocked(out error))
                {
                    document.presets.Remove(created);
                    return false;
                }

                preset = created.Clone();
                return true;
            }
        }

        public static bool SaveDraft(
            LoadoutDraft draft,
            string name,
            out HorusLoadoutPreset preset,
            out string error)
        {
            preset = null;
            if (draft == null)
            {
                error = "Loadout draft is missing.";
                return false;
            }
            return Create(draft.AircraftJsonKey, name, draft.WeaponMountJsonKeys, out preset, out error);
        }

        public static bool Update(
            string presetId,
            LoadoutDraft draft,
            out HorusLoadoutPreset saved,
            out string error)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                saved = null;
                if (draft == null)
                {
                    error = "Loadout draft is missing.";
                    return false;
                }

                HorusLoadoutPreset existing = FindByIdLocked(presetId);
                if (existing == null)
                {
                    error = $"Preset '{presetId}' was not found.";
                    return false;
                }
                if (!Same(existing.aircraftJsonKey, draft.AircraftJsonKey))
                {
                    error = $"Preset belongs to '{existing.aircraftJsonKey}', not '{draft.AircraftJsonKey}'.";
                    return false;
                }

                var value = new HorusLoadoutPreset
                {
                    aircraftJsonKey = existing.aircraftJsonKey,
                    presetId = existing.presetId,
                    name = existing.name,
                    weaponMountJsonKeys = CopyKeys(draft.WeaponMountJsonKeys)
                };
                return Upsert(value, out saved, out error);
            }
        }

        public static bool Upsert(
            HorusLoadoutPreset value,
            out HorusLoadoutPreset saved,
            out string error)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                saved = null;
                if (value == null)
                {
                    error = "Preset is missing.";
                    return false;
                }
                if (!CanWriteLocked(out error)) return false;

                HorusLoadoutPreset existing = FindByIdLocked(value.presetId);
                string ignoredId = existing != null ? existing.presetId : null;
                if (!ValidateIdentityLocked(value.aircraftJsonKey, value.name, ignoredId, out string key, out string cleanName, out error))
                    return false;

                bool created = existing == null;
                HorusLoadoutPreset before = existing?.Clone();
                if (created)
                {
                    existing = new HorusLoadoutPreset
                    {
                        presetId = string.IsNullOrWhiteSpace(value.presetId)
                            ? Guid.NewGuid().ToString("N")
                            : Normalize(value.presetId)
                    };
                    if (FindByIdLocked(existing.presetId) != null)
                    {
                        error = $"Preset id '{existing.presetId}' already exists.";
                        return false;
                    }
                    document.presets.Add(existing);
                }

                existing.aircraftJsonKey = key;
                existing.name = cleanName;
                existing.weaponMountJsonKeys = CopyKeys(value.weaponMountJsonKeys);

                if (!TrySaveLocked(out error))
                {
                    if (created)
                    {
                        document.presets.Remove(existing);
                    }
                    else
                    {
                        existing.aircraftJsonKey = before.aircraftJsonKey;
                        existing.name = before.name;
                        existing.weaponMountJsonKeys = before.weaponMountJsonKeys;
                    }
                    return false;
                }

                saved = existing.Clone();
                return true;
            }
        }

        public static bool Duplicate(
            string presetId,
            string newName,
            out HorusLoadoutPreset duplicate,
            out string error)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                duplicate = null;
                HorusLoadoutPreset source = FindByIdLocked(presetId);
                if (source == null)
                {
                    error = $"Preset '{presetId}' was not found.";
                    return false;
                }
                return Create(source.aircraftJsonKey, newName, source.weaponMountJsonKeys, out duplicate, out error);
            }
        }

        public static bool Rename(string presetId, string newName, out string error)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                if (!CanWriteLocked(out error)) return false;
                HorusLoadoutPreset preset = FindByIdLocked(presetId);
                if (preset == null)
                {
                    error = $"Preset '{presetId}' was not found.";
                    return false;
                }
                if (!ValidateIdentityLocked(preset.aircraftJsonKey, newName, preset.presetId, out _, out string cleanName, out error))
                    return false;

                string oldName = preset.name;
                preset.name = cleanName;
                if (TrySaveLocked(out error)) return true;
                preset.name = oldName;
                return false;
            }
        }

        public static bool Delete(string presetId, out string error)
        {
            lock (Gate)
            {
                EnsureLoadedLocked();
                if (!CanWriteLocked(out error)) return false;
                HorusLoadoutPreset preset = FindByIdLocked(presetId);
                if (preset == null)
                {
                    error = $"Preset '{presetId}' was not found.";
                    return false;
                }

                int index = document.presets.IndexOf(preset);
                document.presets.RemoveAt(index);
                if (TrySaveLocked(out error)) return true;
                document.presets.Insert(index, preset);
                return false;
            }
        }

        public static bool TryCreateDraft(
            AircraftDefinition definition,
            string presetId,
            out LoadoutDraft draft,
            out string error)
        {
            draft = null;
            error = null;
            if (definition == null)
            {
                error = "Aircraft definition is missing.";
                return false;
            }

            lock (Gate)
            {
                EnsureLoadedLocked();
                HorusLoadoutPreset preset = FindByIdLocked(presetId);
                if (preset == null)
                {
                    error = $"Preset '{presetId}' was not found.";
                    return false;
                }
                if (!Same(preset.aircraftJsonKey, definition.jsonKey))
                {
                    error = $"Preset belongs to '{preset.aircraftJsonKey}', not '{definition.jsonKey}'.";
                    return false;
                }

                float fuel = definition.aircraftParameters != null
                    ? Mathf.Clamp01(definition.aircraftParameters.DefaultFuelLevel)
                    : 1f;
                draft = new LoadoutDraft(
                    preset.aircraftJsonKey,
                    LoadoutSourceKind.HorusSavedPreset,
                    preset.weaponMountJsonKeys,
                    fuel,
                    -1,
                    preset.presetId,
                    preset.name);
                return true;
            }
        }

        private static void EnsureLoadedLocked()
        {
            if (!loaded) LoadLocked();
        }

        private static void LoadLocked()
        {
            document = NewDocument();
            loaded = true;
            readOnly = false;
            needsMigrationSave = false;
            invalidFileNeedsBackup = false;
            lastLoadError = null;

            if (!File.Exists(FilePath)) return;
            try
            {
                string json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The preset file is empty.");
                HorusLoadoutPresetDocument loadedDocument = JsonUtility.FromJson<HorusLoadoutPresetDocument>(json);
                if (loadedDocument == null) throw new InvalidDataException("JSON did not contain a preset document.");

                if (loadedDocument.schemaVersion > CurrentSchemaVersion)
                {
                    document = loadedDocument;
                    if (document.presets == null) document.presets = new List<HorusLoadoutPreset>();
                    readOnly = true;
                    lastLoadError = $"Preset schema {loadedDocument.schemaVersion} is newer than supported schema {CurrentSchemaVersion}.";
                    HorusLog.Warning(Subsystem, lastLoadError + " Presets are read-only to avoid data loss.");
                    return;
                }

                document = loadedDocument;
                needsMigrationSave = document.schemaVersion < CurrentSchemaVersion;
                document.schemaVersion = CurrentSchemaVersion;
                SanitizeLoadedDocumentLocked();
                HorusLog.Info(Subsystem, $"Loaded {document.presets.Count} named aircraft loadout preset(s).");
            }
            catch (Exception ex)
            {
                document = NewDocument();
                invalidFileNeedsBackup = true;
                lastLoadError = $"Could not parse '{FilePath}': {ex.Message}";
                HorusLog.Warning(Subsystem, lastLoadError + " Starting with an empty in-memory store; the original will be backed up before any save.");
            }
        }

        private static void SanitizeLoadedDocumentLocked()
        {
            if (document.presets == null)
            {
                document.presets = new List<HorusLoadoutPreset>();
                needsMigrationSave = true;
                return;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = document.presets.Count - 1; i >= 0; i--)
            {
                HorusLoadoutPreset preset = document.presets[i];
                if (preset == null || string.IsNullOrWhiteSpace(preset.aircraftJsonKey) || string.IsNullOrWhiteSpace(preset.name))
                {
                    document.presets.RemoveAt(i);
                    needsMigrationSave = true;
                    continue;
                }

                preset.aircraftJsonKey = Normalize(preset.aircraftJsonKey);
                preset.name = Normalize(preset.name);
                preset.presetId = Normalize(preset.presetId);
                if (string.IsNullOrWhiteSpace(preset.presetId) || !ids.Add(preset.presetId))
                {
                    preset.presetId = Guid.NewGuid().ToString("N");
                    ids.Add(preset.presetId);
                    needsMigrationSave = true;
                }

                if (preset.weaponMountJsonKeys == null)
                {
                    preset.weaponMountJsonKeys = new List<string>();
                    needsMigrationSave = true;
                }
                for (int keyIndex = 0; keyIndex < preset.weaponMountJsonKeys.Count; keyIndex++)
                {
                    string normalized = NormalizeMountKey(preset.weaponMountJsonKeys[keyIndex]);
                    if (normalized != preset.weaponMountJsonKeys[keyIndex]) needsMigrationSave = true;
                    preset.weaponMountJsonKeys[keyIndex] = normalized;
                }

                if (!names.TryGetValue(preset.aircraftJsonKey, out HashSet<string> aircraftNames))
                {
                    aircraftNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    names.Add(preset.aircraftJsonKey, aircraftNames);
                }
                if (!aircraftNames.Add(preset.name))
                {
                    string baseName = preset.name;
                    int suffix = 2;
                    while (!aircraftNames.Add($"{baseName} ({suffix})")) suffix++;
                    preset.name = $"{baseName} ({suffix})";
                    needsMigrationSave = true;
                }
            }
        }

        private static bool ValidateIdentityLocked(
            string aircraftJsonKey,
            string name,
            string ignoredPresetId,
            out string cleanKey,
            out string cleanName,
            out string error)
        {
            cleanKey = Normalize(aircraftJsonKey);
            cleanName = Normalize(name);
            error = null;
            if (string.IsNullOrEmpty(cleanKey))
            {
                error = "Aircraft json key is required.";
                return false;
            }
            if (string.IsNullOrEmpty(cleanName))
            {
                error = "Preset name is required.";
                return false;
            }

            for (int i = 0; i < document.presets.Count; i++)
            {
                HorusLoadoutPreset candidate = document.presets[i];
                if (candidate == null) continue;
                if (Same(candidate.presetId, ignoredPresetId)) continue;
                if (Same(candidate.aircraftJsonKey, cleanKey) && Same(candidate.name, cleanName))
                {
                    error = $"A preset named '{cleanName}' already exists for '{cleanKey}'.";
                    return false;
                }
            }
            return true;
        }

        private static bool CanWriteLocked(out string error)
        {
            if (readOnly)
            {
                error = lastLoadError ?? "Preset file is read-only.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TrySaveLocked(out string error)
        {
            error = null;
            string tempPath = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                if (invalidFileNeedsBackup && File.Exists(FilePath))
                {
                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    string invalidBackup = FilePath + $".invalid-{stamp}.json";
                    File.Copy(FilePath, invalidBackup, false);
                    invalidFileNeedsBackup = false;
                    HorusLog.Warning(Subsystem, $"Backed up invalid preset JSON to '{invalidBackup}'.");
                }

                document.schemaVersion = CurrentSchemaVersion;
                string json = JsonUtility.ToJson(document, true);
                File.WriteAllText(tempPath, json);
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tempPath, FilePath, FilePath + ".bak", true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(tempPath, FilePath, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }

                needsMigrationSave = false;
                lastLoadError = null;
                HorusLog.Verbose(Subsystem, $"Saved {document.presets.Count} preset(s) to '{FilePath}'.");
                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not save loadout presets: {ex.Message}";
                lastLoadError = error;
                HorusLog.Error(Subsystem, error);
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                    // Preserve the original save failure; a stale .tmp is harmless.
                }
                return false;
            }
        }

        private static HorusLoadoutPreset FindByIdLocked(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId)) return null;
            for (int i = 0; i < document.presets.Count; i++)
            {
                if (document.presets[i] != null && Same(document.presets[i].presetId, presetId)) return document.presets[i];
            }
            return null;
        }

        private static HorusLoadoutPresetDocument NewDocument()
        {
            return new HorusLoadoutPresetDocument
            {
                schemaVersion = CurrentSchemaVersion,
                presets = new List<HorusLoadoutPreset>()
            };
        }

        private static List<string> CopyKeys(IEnumerable<string> keys)
        {
            var result = new List<string>();
            if (keys == null) return result;
            foreach (string key in keys) result.Add(NormalizeMountKey(key));
            return result;
        }

        private static string Normalize(string value) => (value ?? "").Trim();

        // The persisted v1 schema uses JSON null for an intentionally empty
        // hardpoint. Runtime drafts accept both null and an empty string.
        private static string NormalizeMountKey(string value)
        {
            string normalized = Normalize(value);
            return normalized.Length == 0 ? null : normalized;
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
