using System;
using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.Economy
{
    public enum RtsFactoryType
    {
        Economy,
        GroundProduction,
        AirProduction,
        NavalProduction,
        DefenseProduction,
        MixedProduction
    }

    /// <summary>
    /// Runtime model representing a live RTS factory.
    /// </summary>
    public class RtsFactory
    {
        public string id;
        public string displayName;
        public string presetName;
        public int factionId;
        public string factionName;

        public RtsFactoryType factoryType;

        // Position data (global coordinates mapped to/from GlobalPosition)
        public float globalX;
        public float globalY;
        public float globalZ;
        public float yaw;

        public bool enabled = true;
        public bool generateIncome = true;
        public float incomePerMinute;

        public bool produceUnits = true;
        public bool consumeBudgetForProduction = true;

        public List<string> productionUnitKeys = new List<string>();
        public int currentProductionIndex;

        public float productionIntervalSeconds;
        public float productionTimer;

        public int maxActiveProducedUnits;
        public List<Unit> activeProducedUnits = new List<Unit>();

        public bool useRallyPoint;
        public float rallyX;
        public float rallyY;
        public float rallyZ;

        public float spawnRadius = 50f;

        // Anchor reference (runtime only)
        public Unit anchorUnit;
        public string anchorUnitName;
        public bool anchorDestroyed;
        public bool isVirtual;
        public string visualBuilding;
        public string lastStatus;
        public float nextAnchorRetryTime;
        public int anchorSpawnFailures;
    }

    // ─── Serialization Models ──────────────────────────────────────────────────

    [Serializable]
    public class SerializableFactory
    {
        public string id;
        public string displayName;
        public string presetName;
        public int factionId;
        public string factionName;
        public string factoryType;

        public float globalX;
        public float globalY;
        public float globalZ;
        public float yaw;

        public bool enabled;
        public bool generateIncome;
        public float incomePerMinute;

        public bool produceUnits;
        public bool consumeBudgetForProduction;

        public List<string> productionUnitKeys = new List<string>();
        public int currentProductionIndex;

        public float productionIntervalSeconds;
        public float productionTimer;

        public int maxActiveProducedUnits;

        public bool useRallyPoint;
        public float rallyX;
        public float rallyY;
        public float rallyZ;

        public float spawnRadius;

        public string anchorUnitName;
        public bool anchorDestroyed;
        public bool isVirtual;
        public string visualBuilding;
        public string lastStatus;
    }

    [Serializable]
    public class SerializableFactoryList
    {
        public int version = 1;
        public List<SerializableFactory> factories = new List<SerializableFactory>();
    }

    [Serializable]
    public class RtsFactoriesSettings
    {
        public bool enableFactories = true;
        public bool factoryIncomeEnabled = true;
        public bool factoryProductionEnabled = true;
        public bool productionConsumesBudget = true;
        public bool autoStartFactories = true;
        public int maxFactoriesPerFaction = 10;
        public bool autoCreateFactoryForAirbaseUnits = false;
        public bool autoCreateFactoryForCarriers = false;
    }

    [Serializable]
    public class FactoryPreset
    {
        public string presetName; // Field mapped from JSON key
        public string type;       // Mapping to RtsFactoryType enum string representation
        public float incomePerMinute;
        public bool produceUnits;
        public float productionIntervalSeconds;
        public int maxActiveProducedUnits;
        public List<string> productionUnitKeys = new List<string>();
        public string visualBuilding;
    }

    [Serializable]
    public class RtsFactoriesConfig
    {
        public int version = 1;
        public RtsFactoriesSettings settings = new RtsFactoriesSettings();
        public List<FactoryPreset> factoryPresets = new List<FactoryPreset>();
    }
}
