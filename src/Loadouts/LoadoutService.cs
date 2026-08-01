using System;
using System.Collections.Generic;
using HorusMod.Logging;
using HorusMod.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace HorusMod.Loadouts
{
    /// <summary>
    /// Converts stable Horus drafts into native Nuclear Option Loadout objects.
    /// This class never mutates a StandardLoadout or another aircraft's loadout.
    /// </summary>
    public static class HorusLoadoutService
    {
        private const string Subsystem = "Loadouts";

        public static Loadout CloneLoadout(Loadout source)
        {
            if (source == null) return null;

            var clone = new Loadout();
            if (source.weapons != null)
                clone.weapons.AddRange(source.weapons);
            return clone;
        }

        public static LoadoutDraft CreateDefaultDraft(AircraftDefinition definition)
        {
            if (definition == null) return null;

            AircraftParameters parameters = definition.aircraftParameters;
            Loadout source = null;
            if (parameters?.loadouts != null)
            {
                if (parameters.loadouts.Count > 1)
                    source = parameters.loadouts[1];
                else if (parameters.loadouts.Count == 1)
                    source = parameters.loadouts[0];
            }

            if (source == null && parameters?.StandardLoadouts != null)
            {
                for (int i = 0; i < parameters.StandardLoadouts.Length; i++)
                {
                    StandardLoadout candidate = parameters.StandardLoadouts[i];
                    if (candidate != null && !candidate.disabled && candidate.loadout != null)
                    {
                        source = candidate.loadout;
                        break;
                    }
                }
            }

            float fuel = parameters != null ? Mathf.Clamp01(parameters.DefaultFuelLevel) : 1f;
            return DraftFromLoadout(
                definition,
                source,
                LoadoutSourceKind.Default,
                fuel,
                -1,
                "default",
                "Default");
        }

        public static bool TryCreateStandardDraft(
            AircraftDefinition definition,
            int presetIndex,
            out LoadoutDraft draft,
            out string error)
        {
            draft = null;
            error = null;
            if (definition == null || definition.aircraftParameters == null)
            {
                error = "Aircraft definition or parameters are missing.";
                return false;
            }

            StandardLoadout[] presets = definition.aircraftParameters.StandardLoadouts;
            if (presets == null || presetIndex < 0 || presetIndex >= presets.Length)
            {
                error = $"Standard loadout index {presetIndex} is out of range.";
                return false;
            }

            StandardLoadout preset = presets[presetIndex];
            if (preset == null || preset.loadout == null)
            {
                error = $"Standard loadout index {presetIndex} has no loadout.";
                return false;
            }

            if (preset.disabled)
            {
                error = $"Standard loadout '{DisplayPresetName(preset, presetIndex)}' is disabled.";
                return false;
            }

            draft = DraftFromLoadout(
                definition,
                preset.loadout,
                LoadoutSourceKind.StandardPreset,
                Mathf.Clamp01(preset.FuelRatio),
                -1,
                presetIndex.ToString(),
                DisplayPresetName(preset, presetIndex));
            return true;
        }

        public static bool TryCreateCurrentAircraftDraft(
            Aircraft aircraft,
            out LoadoutDraft draft,
            out string error)
        {
            draft = null;
            error = null;
            if (aircraft == null || aircraft.definition == null)
            {
                error = "Aircraft or its definition is missing.";
                return false;
            }

            Loadout current = aircraft.Networkloadout ?? aircraft.loadout;
            if (current == null)
            {
                error = "The aircraft has no current loadout to copy.";
                return false;
            }

            LiveryKey livery = aircraft.NetworkLiveryKey;
            int liveryIndex = livery.Type == LiveryKey.KeyType.Builtin ? livery.Index : -1;
            draft = DraftFromLoadout(
                aircraft.definition,
                current,
                LoadoutSourceKind.CopyCurrentAircraft,
                Mathf.Clamp01(aircraft.GetFuelLevel()),
                liveryIndex,
                "current-aircraft",
                "Copy current aircraft");
            return true;
        }

        public static bool TryCreateSessionDraft(
            AircraftDefinition definition,
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

            if (GameManager.aircraftCustomization == null ||
                !GameManager.aircraftCustomization.TryGetValue(definition, out AircraftCustomization customization) ||
                customization == null || customization.loadout == null)
            {
                error = "No current-session customization exists for this aircraft.";
                return false;
            }

            draft = DraftFromLoadout(
                definition,
                customization.loadout,
                LoadoutSourceKind.CurrentSession,
                Mathf.Clamp01(customization.fuelLevel),
                customization.livery,
                "game-session",
                "Current session");
            return true;
        }

        public static LoadoutDraft CreateCustomDraft(
            AircraftDefinition definition,
            IEnumerable<string> weaponMountJsonKeys = null,
            float? fuelRatio = null)
        {
            if (definition == null) return null;

            int count = GetHardpointCount(definition);
            var keys = weaponMountJsonKeys != null
                ? new List<string>(weaponMountJsonKeys)
                : new List<string>();
            while (keys.Count < count) keys.Add("");
            if (keys.Count > count) keys.RemoveRange(count, keys.Count - count);

            float fuel = fuelRatio ?? definition.aircraftParameters?.DefaultFuelLevel ?? 1f;
            return new LoadoutDraft(
                definition.jsonKey,
                LoadoutSourceKind.CustomHardpoints,
                keys,
                Mathf.Clamp01(fuel),
                -1,
                "custom",
                "Custom hardpoints");
        }

        /// <summary>
        /// Verifies that a draft is safe to serialize as a Horus preset. Unlike
        /// trusted native/session sources, persisted JSON may only reference the
        /// weapon options explicitly advertised by each aircraft hardpoint.
        /// </summary>
        public static bool CanPersistAsHorusPreset(
            AircraftDefinition definition,
            LoadoutDraft draft,
            out string error)
        {
            error = null;
            if (!TryGetWeaponManager(definition, out WeaponManager weaponManager, out error))
                return false;
            if (draft == null)
            {
                error = "Loadout draft is missing.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(draft.AircraftJsonKey) &&
                !string.Equals(draft.AircraftJsonKey, definition.jsonKey, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Loadout belongs to '{draft.AircraftJsonKey}', not '{definition.jsonKey}'.";
                return false;
            }

            HardpointSet[] sets = weaponManager.hardpointSets ?? Array.Empty<HardpointSet>();
            int draftCount = draft.WeaponMountJsonKeys?.Count ?? 0;
            if (draft.WeaponMountJsonKeys == null || draftCount != sets.Length)
            {
                error = $"Loadout has {draftCount} hardpoints; aircraft requires {sets.Length}.";
                return false;
            }

            for (int i = 0; i < sets.Length; i++)
            {
                string key = NormalizeKey(draft.WeaponMountJsonKeys[i]);
                if (string.IsNullOrEmpty(key)) continue;
                if (!MountKeyExists(sets[i], key))
                {
                    error = $"Hardpoint {i} mount '{key}' is not advertised in this aircraft's weapon options and cannot be saved as a Horus preset.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Sets a hardpoint by stable mount key. If mirrorSymmetry is true, the
        /// native SymmetryWithPrev relationship is followed where possible.
        /// </summary>
        public static bool TrySetHardpoint(
            LoadoutDraft draft,
            AircraftDefinition definition,
            int hardpointIndex,
            string weaponMountJsonKey,
            bool mirrorSymmetry,
            out string error)
        {
            error = null;
            if (!TryGetWeaponManager(definition, out WeaponManager weaponManager, out error)) return false;
            if (draft == null)
            {
                error = "Loadout draft is missing.";
                return false;
            }

            if (draft.WeaponMountJsonKeys.Count != weaponManager.hardpointSets.Length)
            {
                error = "Draft hardpoint count does not match this aircraft.";
                return false;
            }

            if (hardpointIndex < 0 || hardpointIndex >= weaponManager.hardpointSets.Length)
            {
                error = $"Hardpoint index {hardpointIndex} is out of range.";
                return false;
            }

            string normalizedKey = NormalizeKey(weaponMountJsonKey);
            if (!MountKeyExists(weaponManager.hardpointSets[hardpointIndex], normalizedKey))
            {
                error = $"Mount '{normalizedKey}' is not an option for hardpoint {hardpointIndex}.";
                return false;
            }

            int mirrorIndex = FindSymmetryPartner(weaponManager.hardpointSets, hardpointIndex);
            if (mirrorSymmetry && mirrorIndex >= 0 &&
                !MountKeyExists(weaponManager.hardpointSets[mirrorIndex], normalizedKey))
            {
                error = $"Mount '{normalizedKey}' is not available on symmetry hardpoint {mirrorIndex}.";
                return false;
            }

            draft.WeaponMountJsonKeys[hardpointIndex] = normalizedKey;
            if (mirrorSymmetry && mirrorIndex >= 0)
                draft.WeaponMountJsonKeys[mirrorIndex] = normalizedKey;

            // Mirror the native loadout menu: occupying a hardpoint empties the ones it
            // precludes, so a manually built loadout can never fail preclusion at spawn.
            if (!string.IsNullOrEmpty(normalizedKey))
                ClearPrecludedHardpoints(weaponManager.hardpointSets, draft, hardpointIndex,
                    mirrorSymmetry ? mirrorIndex : -1);

            // Symmetry is an editing convenience, not a global constraint. Native
            // loadouts can legitimately split other linked pairs.
            draft.EnforceSymmetry = false;
            draft.Source = LoadoutSourceKind.CustomHardpoints;
            return true;
        }

        /// <summary>
        /// True when this hardpoint must stay empty because one of its precluding
        /// hardpoints is currently occupied (mirrors HardpointSet.BlockedByOtherHardpoint).
        /// </summary>
        public static bool IsHardpointBlocked(
            AircraftDefinition definition,
            LoadoutDraft draft,
            int hardpointIndex,
            out int blockingIndex)
        {
            blockingIndex = -1;
            if (draft?.WeaponMountJsonKeys == null) return false;
            if (!TryGetWeaponManager(definition, out WeaponManager manager, out _)) return false;
            HardpointSet[] sets = manager.hardpointSets;
            if (sets == null || hardpointIndex < 0 || hardpointIndex >= sets.Length) return false;
            List<byte> precluding = sets[hardpointIndex]?.precludingHardpointSets;
            if (precluding == null) return false;
            for (int i = 0; i < precluding.Count; i++)
            {
                int idx = precluding[i];
                if (idx == hardpointIndex) continue;
                if (idx >= 0 && idx < draft.WeaponMountJsonKeys.Count &&
                    !string.IsNullOrEmpty(NormalizeKey(draft.WeaponMountJsonKeys[idx])))
                {
                    blockingIndex = idx;
                    return true;
                }
            }
            return false;
        }

        private static void ClearPrecludedHardpoints(
            HardpointSet[] sets,
            LoadoutDraft draft,
            int occupiedIndex,
            int mirrorIndex)
        {
            if (sets == null || draft?.WeaponMountJsonKeys == null) return;
            for (int i = 0; i < sets.Length; i++)
            {
                if (i == occupiedIndex || i == mirrorIndex) continue;
                List<byte> precluding = sets[i]?.precludingHardpointSets;
                if (precluding == null) continue;
                if ((occupiedIndex >= 0 && precluding.Contains((byte)occupiedIndex)) ||
                    (mirrorIndex >= 0 && precluding.Contains((byte)mirrorIndex)))
                {
                    if (i < draft.WeaponMountJsonKeys.Count) draft.WeaponMountJsonKeys[i] = "";
                }
            }
        }

        public static IReadOnlyList<WeaponMount> GetLegalMounts(
            AircraftDefinition definition,
            int hardpointIndex,
            FactionHQ hq)
        {
            var result = new List<WeaponMount>();
            if (!TryGetWeaponManager(definition, out WeaponManager manager, out _) ||
                hardpointIndex < 0 || hardpointIndex >= manager.hardpointSets.Length)
                return result;

            HardpointSet set = manager.hardpointSets[hardpointIndex];
            if (set?.weaponOptions == null) return result;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount mount = set.weaponOptions[i];
                if (mount != null &&
                    WeaponChecker.MountAllowedHQ(mount, hq) &&
                    IsNuclearAllowedByMission(mount))
                    result.Add(mount);
            }
            return result;
        }

        public static IReadOnlyList<LoadoutDraft> GetValidStandardDrafts(
            AircraftDefinition definition,
            FactionHQ hq)
        {
            var result = new List<LoadoutDraft>();
            StandardLoadout[] presets = definition?.aircraftParameters?.StandardLoadouts;
            if (presets == null) return result;

            for (int i = 0; i < presets.Length; i++)
            {
                if (!TryCreateStandardDraft(definition, i, out LoadoutDraft draft, out _)) continue;
                if (ResolveForSpawn(definition, hq, draft).Success) result.Add(draft);
            }
            return result;
        }

        public static LoadoutApplyResult ResolveDefaultForSpawn(
            AircraftDefinition definition,
            FactionHQ hq)
        {
            return ResolveForSpawn(definition, hq, CreateDefaultDraft(definition));
        }

        public static LoadoutApplyResult ResolveStandardForSpawn(
            AircraftDefinition definition,
            FactionHQ hq,
            int presetIndex)
        {
            if (!TryCreateStandardDraft(definition, presetIndex, out LoadoutDraft draft, out string error))
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.InvalidArgument, error);
            return ResolveForSpawn(definition, hq, draft);
        }

        public static LoadoutApplyResult ResolveRandomStandardForSpawn(
            AircraftDefinition definition,
            FactionHQ hq)
        {
            IReadOnlyList<LoadoutDraft> valid = GetValidStandardDrafts(definition, hq);
            if (valid.Count == 0)
            {
                return LoadoutApplyResult.Fail(
                    LoadoutApplyStatus.ValidationFailed,
                    "This aircraft has no valid standard loadout for the selected faction/HQ.");
            }

            return ResolveForSpawn(definition, hq, valid[UnityEngine.Random.Range(0, valid.Count)]);
        }

        public static LoadoutApplyResult ResolveForSpawn(
            AircraftDefinition definition,
            FactionHQ hq,
            LoadoutDraft draft)
        {
            if (!TryGetWeaponManager(definition, out WeaponManager manager, out string error))
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.InvalidArgument, error);
            return Resolve(definition, manager, hq, draft);
        }

        public static bool ResolveForSpawn(
            AircraftDefinition definition,
            FactionHQ hq,
            LoadoutDraft draft,
            out Loadout loadout,
            out float fuelRatio,
            out string error)
        {
            LoadoutApplyResult result = ResolveForSpawn(definition, hq, draft);
            loadout = result.ResolvedLoadout;
            fuelRatio = result.FuelRatio;
            error = result.Success ? null : result.Message;
            return result.Success;
        }

        public static LoadoutApplyResult ApplyToAircraft(Aircraft aircraft, LoadoutDraft draft)
        {
            if (!HorusPermissions.CanSpawn())
            {
                return LoadoutApplyResult.Fail(
                    LoadoutApplyStatus.NotAuthorized,
                    "Only the single-player instance or multiplayer host can change aircraft loadouts.");
            }

            if (aircraft == null || aircraft.definition == null || aircraft.weaponManager == null)
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.InvalidArgument, "Aircraft loadout context is incomplete.");

            LoadoutApplyResult resolved = Resolve(aircraft.definition, aircraft.weaponManager, aircraft.NetworkHQ, draft);
            if (!resolved.Success) return resolved;

            try
            {
                // Resolve already creates a new object. Clone once more so the result
                // can safely be retained by callers without sharing the SyncVar value.
                Loadout applied = CloneLoadout(resolved.ResolvedLoadout);
                aircraft.Networkloadout = applied;
                HorusLog.Verbose(
                    Subsystem,
                    $"Applied {applied.weapons.Count}-hardpoint loadout to '{aircraft.unitName}'.");
                return new LoadoutApplyResult(
                    LoadoutApplyStatus.Success,
                    "Loadout applied.",
                    CloneLoadout(applied),
                    resolved.FuelRatio,
                    resolved.LiveryIndex,
                    resolved.Issues);
            }
            catch (Exception ex)
            {
                HorusLog.Error(Subsystem, $"Failed to apply loadout to '{aircraft.unitName}': {ex.Message}");
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.ApplyFailed, $"Could not apply loadout: {ex.Message}");
            }
        }

        public static LoadoutApplyResult ApplyToAircraft(Aircraft aircraft, Loadout loadout)
        {
            if (aircraft == null || aircraft.definition == null)
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.InvalidArgument, "Aircraft is missing.");

            LoadoutDraft draft = DraftFromLoadout(
                aircraft.definition,
                loadout,
                LoadoutSourceKind.CurrentSession,
                aircraft.GetFuelLevel(),
                -1,
                "native-loadout",
                "Native loadout");
            return ApplyToAircraft(aircraft, draft);
        }

        public static LoadoutApplyResult ApplyStandardPreset(Aircraft aircraft, int presetIndex)
        {
            if (aircraft == null || aircraft.definition == null)
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.InvalidArgument, "Aircraft is missing.");
            if (!TryCreateStandardDraft(aircraft.definition, presetIndex, out LoadoutDraft draft, out string error))
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.InvalidArgument, error);
            return ApplyToAircraft(aircraft, draft);
        }

        private static LoadoutApplyResult Resolve(
            AircraftDefinition definition,
            WeaponManager weaponManager,
            FactionHQ hq,
            LoadoutDraft draft)
        {
            if (definition == null || weaponManager == null || draft == null)
                return LoadoutApplyResult.Fail(LoadoutApplyStatus.InvalidArgument, "Definition, weapon manager, or loadout draft is missing.");

            if (!string.IsNullOrWhiteSpace(draft.AircraftJsonKey) &&
                !string.Equals(draft.AircraftJsonKey, definition.jsonKey, StringComparison.OrdinalIgnoreCase))
            {
                return LoadoutApplyResult.Fail(
                    LoadoutApplyStatus.DefinitionMismatch,
                    $"Loadout belongs to '{draft.AircraftJsonKey}', not '{definition.jsonKey}'.");
            }

            HardpointSet[] sets = weaponManager.hardpointSets ?? Array.Empty<HardpointSet>();
            if (draft.WeaponMountJsonKeys == null || draft.WeaponMountJsonKeys.Count != sets.Length)
            {
                return LoadoutApplyResult.Fail(
                    LoadoutApplyStatus.InvalidHardpointCount,
                    $"Loadout has {draft.WeaponMountJsonKeys?.Count ?? 0} hardpoints; aircraft requires {sets.Length}.");
            }

            var issues = new List<LoadoutValidationIssue>();
            var resolved = new Loadout();
            for (int i = 0; i < sets.Length; i++)
            {
                HardpointSet set = sets[i];
                string key = NormalizeKey(draft.WeaponMountJsonKeys[i]);
                WeaponMount mount = ResolveMount(set, key, draft.Source, i, issues);
                resolved.weapons.Add(mount);

                if (mount == null) continue;
                if (!WeaponChecker.MountAllowedHQ(mount, hq))
                {
                    issues.Add(Error(i, "mount-restricted", $"Mount '{DisplayMount(mount)}' is disabled, event-blocked, or restricted by this HQ."));
                }

                ValidateNuclearMissionRules(mount, i, issues);
                if (HorusPermissions.IsMultiplayer() && !IsNetworkRegistered(mount))
                {
                    issues.Add(Error(i, "mount-not-network-registered",
                        $"Mount '{DisplayMount(mount)}' is not registered for multiplayer serialization."));
                }
            }

            ValidatePreclusions(sets, resolved, issues);

            if (!HasErrors(issues) && hq != null)
            {
                try
                {
                    if (!resolved.AllowedByHQ(weaponManager, hq))
                    {
                        issues.Add(Error(-1, "hq-loadout-rejected", "The selected HQ cannot supply this loadout or its required warheads."));
                    }
                }
                catch (Exception ex)
                {
                    issues.Add(Error(-1, "hq-validation-failed", $"HQ validation failed: {ex.Message}"));
                }
            }

            if (HasErrors(issues))
            {
                string message = issues.Count == 1
                    ? issues[0].Message
                    : $"Loadout validation failed with {CountErrors(issues)} error(s).";
                return new LoadoutApplyResult(
                    LoadoutApplyStatus.ValidationFailed,
                    message,
                    null,
                    Mathf.Clamp01(draft.FuelRatio),
                    draft.LiveryIndex,
                    issues);
            }

            return new LoadoutApplyResult(
                LoadoutApplyStatus.Success,
                "Loadout resolved.",
                CloneLoadout(resolved),
                Mathf.Clamp01(draft.FuelRatio),
                draft.LiveryIndex,
                issues);
        }

        private static WeaponMount ResolveMount(
            HardpointSet set,
            string key,
            LoadoutSourceKind source,
            int hardpointIndex,
            List<LoadoutValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (set?.weaponOptions == null)
            {
                issues.Add(Error(hardpointIndex, "hardpoint-options-missing", "Hardpoint exposes no weapon options."));
                return null;
            }

            WeaponMount match = null;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount candidate = set.weaponOptions[i];
                if (candidate == null || !string.Equals(candidate.jsonKey, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (match != null && match != candidate)
                {
                    issues.Add(Error(hardpointIndex, "duplicate-mount-key", $"Mount key '{key}' is ambiguous on this hardpoint."));
                    return null;
                }
                match = candidate;
            }

            if (match == null && IsTrustedNativeSource(source) && Encyclopedia.WeaponLookup != null &&
                Encyclopedia.WeaponLookup.TryGetValue(key, out WeaponMount nativeMount) && nativeMount != null)
            {
                // Nuclear Option itself supports hidden/native mounts when loading a
                // saved loadout. Preserve those for trusted in-memory sources, but do
                // not expose arbitrary off-hardpoint mounts to manual/saved Horus JSON.
                match = nativeMount;
            }

            if (match == null)
                issues.Add(Error(hardpointIndex, "mount-not-allowed", $"Mount '{key}' is not a legal option for this hardpoint."));
            return match;
        }

        private static bool IsTrustedNativeSource(LoadoutSourceKind source)
        {
            return source == LoadoutSourceKind.Default ||
                   source == LoadoutSourceKind.StandardPreset ||
                   source == LoadoutSourceKind.CurrentSession ||
                   source == LoadoutSourceKind.CopyCurrentAircraft ||
                   source == LoadoutSourceKind.RandomStandardPreset;
        }

        private static bool IsNetworkRegistered(WeaponMount mount)
        {
            if (mount == null || Encyclopedia.i?.IndexLookup == null) return mount == null;
            for (int i = 0; i < Encyclopedia.i.IndexLookup.Count; i++)
                if (ReferenceEquals(Encyclopedia.i.IndexLookup[i], mount)) return true;
            return false;
        }

        private static void ValidatePreclusions(
            HardpointSet[] sets,
            Loadout loadout,
            List<LoadoutValidationIssue> issues)
        {
            for (int i = 0; i < sets.Length; i++)
            {
                if (loadout.weapons[i] == null) continue;
                List<byte> precluding = sets[i]?.precludingHardpointSets;
                if (precluding == null) continue;
                for (int j = 0; j < precluding.Count; j++)
                {
                    int blockedIndex = precluding[j];
                    if (blockedIndex >= 0 && blockedIndex < loadout.weapons.Count && loadout.weapons[blockedIndex] != null)
                    {
                        issues.Add(Error(
                            i,
                            "hardpoint-conflict",
                            $"Hardpoint {i} conflicts with occupied hardpoint {blockedIndex}."));
                        break;
                    }
                }
            }
        }

        private static void ValidateSymmetry(
            HardpointSet[] sets,
            Loadout loadout,
            bool enforceSymmetry,
            List<LoadoutValidationIssue> issues)
        {
            if (!enforceSymmetry) return;
            for (int i = 1; i < sets.Length; i++)
            {
                if (sets[i] == null || !sets[i].SymmetryWithPrev) continue;
                string current = loadout.weapons[i]?.jsonKey ?? "";
                string previous = loadout.weapons[i - 1]?.jsonKey ?? "";
                if (!string.Equals(current, previous, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Error(
                        i,
                        "symmetry-mismatch",
                        $"Hardpoint {i} must match symmetry partner {i - 1}, or symmetry must be split."));
                }
            }
        }

        private static void ValidateNuclearMissionRules(
            WeaponMount mount,
            int hardpointIndex,
            List<LoadoutValidationIssue> issues)
        {
            if (mount?.info == null || !mount.info.nuclear) return;
            try
            {
                if (!MissionManager.AllowTactical())
                {
                    issues.Add(Error(hardpointIndex, "tactical-disabled", "Tactical nuclear weapons are not enabled at the current escalation."));
                }
                else if (mount.info.strategic && !MissionManager.AllowStrategic())
                {
                    issues.Add(Error(hardpointIndex, "strategic-disabled", "Strategic nuclear weapons are not enabled at the current escalation."));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new LoadoutValidationIssue(
                    LoadoutIssueSeverity.Warning,
                    hardpointIndex,
                    "nuclear-validation-unavailable",
                    $"Could not inspect mission nuclear rules: {ex.Message}"));
            }
        }

        private static bool IsNuclearAllowedByMission(WeaponMount mount)
        {
            if (mount?.info == null || !mount.info.nuclear) return true;
            try
            {
                return MissionManager.AllowTactical() &&
                    (!mount.info.strategic || MissionManager.AllowStrategic());
            }
            catch
            {
                // Catalogs can be inspected before MissionManager exists. Resolution
                // performs the same check again once an actual mission is available.
                return true;
            }
        }

        private static LoadoutDraft DraftFromLoadout(
            AircraftDefinition definition,
            Loadout loadout,
            LoadoutSourceKind source,
            float fuelRatio,
            int liveryIndex,
            string sourceId,
            string name)
        {
            var keys = new List<string>();
            if (loadout?.weapons != null)
            {
                for (int i = 0; i < loadout.weapons.Count; i++)
                    keys.Add(loadout.weapons[i] != null ? loadout.weapons[i].jsonKey ?? "" : "");
            }
            else
            {
                int count = GetHardpointCount(definition);
                for (int i = 0; i < count; i++) keys.Add("");
            }

            return new LoadoutDraft(
                definition?.jsonKey,
                source,
                keys,
                Mathf.Clamp01(fuelRatio),
                liveryIndex,
                sourceId,
                name);
        }

        private static bool TryGetWeaponManager(
            AircraftDefinition definition,
            out WeaponManager weaponManager,
            out string error)
        {
            weaponManager = null;
            error = null;
            if (definition == null)
            {
                error = "Aircraft definition is missing.";
                return false;
            }
            if (definition.unitPrefab == null)
            {
                error = $"Aircraft '{definition.jsonKey}' has no prefab.";
                return false;
            }

            Aircraft prefabAircraft = definition.unitPrefab.GetComponent<Aircraft>();
            weaponManager = prefabAircraft != null ? prefabAircraft.weaponManager : definition.unitPrefab.GetComponent<WeaponManager>();
            if (weaponManager == null)
            {
                error = $"Aircraft '{definition.jsonKey}' exposes no WeaponManager/hardpoints.";
                return false;
            }
            return true;
        }

        private static int GetHardpointCount(AircraftDefinition definition)
        {
            return TryGetWeaponManager(definition, out WeaponManager manager, out _)
                ? manager.hardpointSets?.Length ?? 0
                : 0;
        }

        private static int FindSymmetryPartner(HardpointSet[] sets, int index)
        {
            if (index > 0 && sets[index] != null && sets[index].SymmetryWithPrev) return index - 1;
            int next = index + 1;
            if (next < sets.Length && sets[next] != null && sets[next].SymmetryWithPrev) return next;
            return -1;
        }

        private static bool MountKeyExists(HardpointSet set, string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            if (set?.weaponOptions == null) return false;
            for (int i = 0; i < set.weaponOptions.Count; i++)
            {
                WeaponMount mount = set.weaponOptions[i];
                if (mount != null && string.Equals(mount.jsonKey, key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool HasErrors(List<LoadoutValidationIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Severity == LoadoutIssueSeverity.Error) return true;
            return false;
        }

        private static int CountErrors(List<LoadoutValidationIssue> issues)
        {
            int count = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Severity == LoadoutIssueSeverity.Error) count++;
            return count;
        }

        private static LoadoutValidationIssue Error(int hardpointIndex, string code, string message)
        {
            return new LoadoutValidationIssue(LoadoutIssueSeverity.Error, hardpointIndex, code, message);
        }

        private static string NormalizeKey(string key) => (key ?? "").Trim();

        private static string DisplayMount(WeaponMount mount)
        {
            if (mount == null) return "Empty";
            if (!string.IsNullOrWhiteSpace(mount.mountName)) return mount.mountName;
            if (!string.IsNullOrWhiteSpace(mount.jsonKey)) return mount.jsonKey;
            return mount.name;
        }

        private static string DisplayPresetName(StandardLoadout preset, int index)
        {
            return !string.IsNullOrWhiteSpace(preset?.Name) ? preset.Name.Trim() : $"Preset {index}";
        }
    }
}
