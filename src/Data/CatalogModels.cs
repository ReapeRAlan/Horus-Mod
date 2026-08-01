using System;
using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.Data
{
    /// <summary>
    /// Concrete route the game spawner must use for a definition. This is kept
    /// separate from placement so a floating supply container is never treated
    /// as a ship merely because it is placed at sea.
    /// </summary>
    public enum SpawnKind
    {
        Aircraft = 0,
        Vehicle = 1,
        Ship = 2,
        Building = 3,
        Scenery = 4,
        Missile = 5,
        Container = 6,
        Other = 7
    }

    public enum PlacementSurface
    {
        Ground = 0,
        Air = 1,
        Sea = 2,
        Free = 3
    }

    public enum NetworkRegistration
    {
        NetworkRegistered = 0,
        LookupOnly = 1
    }

    public enum CapabilityState
    {
        No = 0,
        Yes = 1,
        Unknown = 2
    }

    [Flags]
    public enum CatalogFlags
    {
        None = 0,
        Disabled = 1 << 0,
        Event = 1 << 1,
        Unlabeled = 1 << 2,
        LookupOnly = 1 << 3,
        DuplicateJsonKey = 1 << 4,
        Logistics = 1 << 5,
        Ammo = 1 << 6,
        NavalResupply = 1 << 7,
        Fuel = 1 << 8,
        Storage = 1 << 9,
        LiveOrdnance = 1 << 10,
        Strategic = 1 << 11,
        Experimental = 1 << 12,
        Modded = 1 << 13,
        Nuclear = 1 << 14
    }

    /// <summary>
    /// Rich catalog model used by v1.3 features. UnitEntry derives from this
    /// type so existing browser and favorites code can continue to use the old
    /// API without adapters.
    /// </summary>
    public class CatalogEntry
    {
        // v1.2-compatible fields.
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

        // v1.3 catalog metadata.
        public string JsonKey;
        public string Source;
        public SpawnKind SpawnKind;
        public PlacementSurface PlacementSurface;
        public NetworkRegistration Registration;
        public CatalogFlags Flags;
        public SupplyCapabilities Supply;
        public bool AllowedInCurrentMode;

        public bool IsNetworkRegistered => Registration == NetworkRegistration.NetworkRegistered;
        public bool IsLookupOnly => Registration == NetworkRegistration.LookupOnly;
        public bool HasKeyConflict => (Flags & CatalogFlags.DuplicateJsonKey) != 0;
        public bool IsDisabled => (Flags & CatalogFlags.Disabled) != 0;
        public bool IsEventContent => (Flags & CatalogFlags.Event) != 0;
        public bool IsUnlabeled => (Flags & CatalogFlags.Unlabeled) != 0;
        public bool IsLiveOrdnance => (Flags & CatalogFlags.LiveOrdnance) != 0;
    }

    /// <summary>Backward-compatible entry type retained for all existing callers.</summary>
    public sealed class UnitEntry : CatalogEntry
    {
    }

    public sealed class CatalogKeyConflict
    {
        private readonly List<UnitEntry> entries;

        internal CatalogKeyConflict(string jsonKey, List<UnitEntry> conflictingEntries)
        {
            JsonKey = jsonKey;
            entries = conflictingEntries;
        }

        public string JsonKey { get; }
        public IReadOnlyList<UnitEntry> Entries => entries;
    }
}
