using System;
using System.Collections.Generic;
using UnityEngine;
using HorusMod.Logging;

namespace HorusMod.Economy
{
    /// <summary>
    /// The two top-level Horus modes.
    /// </summary>
    public enum HorusMode
    {
        Sandbox = 0,
        RtsCommander = 1
    }

    /// <summary>
    /// RTS deployment restriction level.
    /// </summary>
    public enum RtsDeployMode
    {
        /// <summary>Place anywhere on the map but still pay.</summary>
        FreePlacementPaid = 0,
        /// <summary>Must deploy within BaseDeploymentRadius of a friendly building/carrier.</summary>
        StrictBaseDeployment = 1
    }

    // ─── JSON-Serializable Config Models (Unity JsonUtility) ─────────────────────

    [Serializable]
    public class FactionBudgetEntry
    {
        public string factionName;
        public float startingBudget;
        public float incomePerTick;
        public int unitCap;
    }

    [Serializable]
    public class CategoryCostEntry
    {
        public string category; // "Aircraft", "Vehicle", "Ship", "Building", "Scenery"
        public float fallbackCost;
    }

    [Serializable]
    public class UnitCostOverride
    {
        public string jsonKey;
        // Legacy field retained only so existing configs deserialize. New entries
        // should use jsonKey; matching is always performed against def.jsonKey.
        public string unitName;
        public float cost;
    }

    /// <summary>
    /// Root JSON structure for BepInEx/config/HorusMod/rts_economy.json
    /// </summary>
    [Serializable]
    public class RtsEconomyConfig
    {
        public float incomeTickSeconds = 5f;
        public float unitCostMultiplier = 1f;
        public List<FactionBudgetEntry> factionBudgets = new List<FactionBudgetEntry>();
        // Kept for backwards-compatible deserialization. Native UnitDefinition.value
        // is now the source of truth; category fallbacks are no longer consulted.
        public List<CategoryCostEntry> categoryCosts = new List<CategoryCostEntry>();
        // Entries are matched against UnitDefinition.jsonKey.
        public List<UnitCostOverride> unitCostOverrides = new List<UnitCostOverride>();
    }

    // ─── Runtime State Models ────────────────────────────────────────────────────

    /// <summary>
    /// Live economy state for one faction during a match.
    /// </summary>
    public class FactionEconomyState
    {
        public string FactionName;
        public float Budget;
        public float IncomePerTick;
        public int UnitCap;
        public int ActiveUnitCount;

        /// <summary>Units currently alive that were spawned under RTS mode for this faction.</summary>
        public readonly List<Unit> TrackedUnits = new List<Unit>();

        public bool IsOverCap => UnitCap > 0 && ActiveUnitCount >= UnitCap;

        public void CleanDeadUnits()
        {
            int removed = TrackedUnits.RemoveAll(u => u == null || u.gameObject == null || u.disabled || u.unitState == Unit.UnitState.Destroyed);
            ActiveUnitCount = TrackedUnits.Count;
            if (removed > 0)
            {
                HorusLog.Info("Economy", $"[RTS Economy] {FactionName}: cleaned {removed} dead units. Active={ActiveUnitCount}/{UnitCap}");
            }
        }
    }

    /// <summary>
    /// Represents a pending spawn purchase before it is committed.
    /// </summary>
    public class RtsTransaction
    {
        public UnitDefinition Definition;
        public int FactionIndex;
        public float Cost;
        public bool IsValid;
        public string DenialReason;

        // For group purchases
        public List<UnitDefinition> GroupDefinitions;
        public float GroupTotalCost;

        public bool IsGroupTransaction => GroupDefinitions != null && GroupDefinitions.Count > 0;
    }
}
