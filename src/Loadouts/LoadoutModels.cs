using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;

namespace HorusMod.Loadouts
{
    /// <summary>
    /// Identifies where a Horus loadout draft came from. A draft contains stable
    /// json keys rather than references to mutable Unity ScriptableObjects.
    /// </summary>
    public enum LoadoutSourceKind
    {
        Default = 0,
        StandardPreset = 1,
        CurrentSession = 2,
        HorusSavedPreset = 3,
        CopyCurrentAircraft = 4,
        CustomHardpoints = 5,
        RandomStandardPreset = 6
    }

    public enum LoadoutApplyStatus
    {
        Success = 0,
        InvalidArgument = 1,
        NotAuthorized = 2,
        DefinitionMismatch = 3,
        NoHardpoints = 4,
        InvalidHardpointCount = 5,
        ValidationFailed = 6,
        ApplyFailed = 7
    }

    public enum LoadoutIssueSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>A validation problem associated with one hardpoint, or -1 for the whole draft.</summary>
    public sealed class LoadoutValidationIssue
    {
        public LoadoutIssueSeverity Severity { get; }
        public int HardpointIndex { get; }
        public string Code { get; }
        public string Message { get; }

        public LoadoutValidationIssue(
            LoadoutIssueSeverity severity,
            int hardpointIndex,
            string code,
            string message)
        {
            Severity = severity;
            HardpointIndex = hardpointIndex;
            Code = code ?? "unknown";
            Message = message ?? "Unknown loadout validation issue.";
        }

        public override string ToString()
        {
            string location = HardpointIndex >= 0 ? $"Hardpoint {HardpointIndex}: " : "";
            return $"{location}{Message}";
        }
    }

    /// <summary>
    /// Stable, editable representation of an aircraft loadout. Empty/null mount
    /// keys mean that the corresponding hardpoint set should be empty.
    /// </summary>
    public sealed class LoadoutDraft
    {
        public string AircraftJsonKey { get; set; }
        public LoadoutSourceKind Source { get; set; }
        public string SourceId { get; set; }
        public string Name { get; set; }
        public List<string> WeaponMountJsonKeys { get; }

        /// <summary>Fuel ratio to pass to the native aircraft spawn path.</summary>
        public float FuelRatio { get; set; }

        /// <summary>
        /// Built-in livery index when supplied by the source, otherwise -1.
        /// Livery editing remains separate from weapon validation.
        /// </summary>
        public int LiveryIndex { get; set; }

        /// <summary>
        /// When true, changing a hardpoint through HorusLoadoutService.TrySetHardpoint
        /// also mirrors the linked symmetry hardpoint. Native Nuclear Option UI
        /// allows symmetry to be split, so validation does not require equality.
        /// </summary>
        public bool EnforceSymmetry { get; set; }

        public int HardpointCount => WeaponMountJsonKeys.Count;

        public LoadoutDraft(
            string aircraftJsonKey,
            LoadoutSourceKind source,
            IEnumerable<string> weaponMountJsonKeys,
            float fuelRatio = 1f,
            int liveryIndex = -1,
            string sourceId = null,
            string name = null)
        {
            AircraftJsonKey = aircraftJsonKey ?? "";
            Source = source;
            SourceId = sourceId ?? "";
            Name = name ?? "";
            FuelRatio = fuelRatio;
            LiveryIndex = liveryIndex;
            WeaponMountJsonKeys = weaponMountJsonKeys != null
                ? new List<string>(weaponMountJsonKeys)
                : new List<string>();
        }

        public LoadoutDraft Clone()
        {
            return new LoadoutDraft(
                AircraftJsonKey,
                Source,
                WeaponMountJsonKeys,
                FuelRatio,
                LiveryIndex,
                SourceId,
                Name)
            {
                EnforceSymmetry = EnforceSymmetry
            };
        }
    }

    /// <summary>
    /// Detailed result returned by both resolution and application. A successful
    /// resolution always owns a fresh Loadout instance and list.
    /// </summary>
    public sealed class LoadoutApplyResult
    {
        private readonly List<LoadoutValidationIssue> issues;

        public bool Success => Status == LoadoutApplyStatus.Success;
        public LoadoutApplyStatus Status { get; }
        public string Message { get; }
        public Loadout ResolvedLoadout { get; }
        public float FuelRatio { get; }
        public int LiveryIndex { get; }
        public IReadOnlyList<LoadoutValidationIssue> Issues => issues;

        internal LoadoutApplyResult(
            LoadoutApplyStatus status,
            string message,
            Loadout resolvedLoadout,
            float fuelRatio,
            int liveryIndex,
            IEnumerable<LoadoutValidationIssue> validationIssues = null)
        {
            Status = status;
            Message = message ?? "";
            ResolvedLoadout = resolvedLoadout;
            FuelRatio = fuelRatio;
            LiveryIndex = liveryIndex;
            issues = validationIssues != null
                ? new List<LoadoutValidationIssue>(validationIssues)
                : new List<LoadoutValidationIssue>();
        }

        internal static LoadoutApplyResult Fail(
            LoadoutApplyStatus status,
            string message,
            IEnumerable<LoadoutValidationIssue> issues = null)
        {
            return new LoadoutApplyResult(status, message, null, 1f, -1, issues);
        }
    }
}
