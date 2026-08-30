using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HorusMod.Data;
using HorusMod.Logging;
using HorusMod.Spawning;
using Newtonsoft.Json;
using UnityEngine;

namespace HorusMod.Economy
{
    /// <summary>Headless factory runtime. It deliberately has no HorusManager or UI dependency.</summary>
    public sealed class RtsFactoryManager
    {
        public static RtsFactoryManager Instance { get; private set; }
        public static Action<Unit> UnitProduced { get; set; }
        public readonly List<RtsFactory> activeFactories = new List<RtsFactory>();

        private readonly string instancesPath = Path.Combine(Paths.ConfigPath, "HorusMod", "rts_factories_server.json");
        private readonly string configPath = Path.Combine(Paths.ConfigPath, "HorusMod", "rts_factories.json");
        private RtsFactoriesConfig config;
        private float incomeAccumulator;

        public RtsFactoriesConfig Config => config;

        public RtsFactoryManager()
        {
            Instance = this;
            LoadFactoryConfig();
        }

        public void InitializeMatchFactories() => LoadInstances();
        public void ResetMatchFactories() => activeFactories.Clear();

        public void Tick()
        {
            RtsEconomyManager economy=RtsEconomyManager.Instance;
            if (economy?.CurrentMode != HorusMode.RtsCommander||config?.settings==null||!config.settings.enableFactories) return;
            if(config.settings.factoryIncomeEnabled)
            {
                incomeAccumulator+=Time.deltaTime;
                if(incomeAccumulator>=5f){float seconds=incomeAccumulator;incomeAccumulator=0f;foreach(RtsFactory factory in activeFactories)if(factory!=null&&factory.enabled&&factory.generateIncome&&factory.incomePerMinute>0f)economy.AdjustBudget(factory.factionId,(factory.incomePerMinute/60f)*seconds);}
            }
            if(config.settings.factoryProductionEnabled)for (int i = 0; i < activeFactories.Count; i++) TickFactory(activeFactories[i],economy);
        }

        private void TickFactory(RtsFactory factory,RtsEconomyManager economy)
        {
            if (factory == null || !factory.enabled || !factory.produceUnits || factory.productionUnitKeys.Count == 0) return;
            factory.activeProducedUnits.RemoveAll(unit => unit == null || unit.Networkdisabled);
            if (factory.activeProducedUnits.Count >= Math.Max(1, factory.maxActiveProducedUnits)) return;

            if (factory.currentProductionIndex < 0 || factory.currentProductionIndex >= factory.productionUnitKeys.Count)
                factory.currentProductionIndex = 0;
            string key = factory.productionUnitKeys[factory.currentProductionIndex];
            UnitEntry entry = UnitCatalog.Find(key);
            FactionSlot faction = FactionSlot.Resolve(factory.factionId);
            if (entry?.Def == null || !faction.IsValid||!CanQueueDefinition(factory,entry.Def)){factory.lastStatus="Invalid production definition: "+key;factory.currentProductionIndex=(factory.currentProductionIndex+1)%factory.productionUnitKeys.Count;return;}
            bool consumes=config.settings.productionConsumesBudget&&factory.consumeBudgetForProduction;
            RtsTransaction transaction=consumes?economy.CreateTransaction(entry.Def,factory.factionId):null;
            if(consumes&&!transaction.IsValid){factory.lastStatus=transaction.DenialReason;return;}
            FactionEconomyState factionState=economy.GetFactionState(factory.factionId);
            if(!consumes&&HorusMod.HorusPlugin.EnableRtsUnitCap?.Value==true&&factionState!=null&&factionState.ActiveUnitCount>=factionState.UnitCap){factory.lastStatus="Unit cap reached";return;}
            factory.productionTimer += Time.deltaTime;
            if (factory.productionTimer < Math.Max(1f, factory.productionIntervalSeconds)){factory.lastStatus="Building "+(entry.Def.unitName??key);return;}
            factory.productionTimer = 0f;

            float radians = factory.yaw * Mathf.Deg2Rad;
            float radius = Math.Max(5f, factory.spawnRadius);
            var offset = new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius);
            var request = new HorusSpawnRequest
            {
                Definition = entry.Def,
                Position = new GlobalPosition(factory.globalX + offset.x, factory.globalY + offset.y, factory.globalZ + offset.z),
                Rotation = Quaternion.Euler(0f, factory.yaw, 0f),
                HQ = faction.HQ,
                Surface = entry.PlacementSurface,
                UniqueName = "horus_factory_" + factory.id + "_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Stationary = false,
                Skill = 0.5f
            };
            HorusSpawnResult spawned = HorusSpawnService.Spawn(request);
            if (spawned.Success)
            {
                factory.activeProducedUnits.Add(spawned.Unit);
                UnitProduced?.Invoke(spawned.Unit);
                if(consumes)economy.CommitTransaction(transaction,spawned.Unit);
                else if(factionState!=null){factionState.TrackedUnits.Add(spawned.Unit);factionState.ActiveUnitCount=factionState.TrackedUnits.Count;}
                if (factory.useRallyPoint)
                    HorusMod.Interaction.HorusOrders.TrySetDestination(spawned.Unit,
                        new GlobalPosition(factory.rallyX, factory.rallyY, factory.rallyZ), true, out _);
                factory.currentProductionIndex=(factory.currentProductionIndex+1)%factory.productionUnitKeys.Count;
                factory.lastStatus="Produced "+(entry.Def.unitName??key);
            }
            else factory.lastStatus="Spawn failed: "+spawned.Message;
        }

        public bool CanUseFactoryFaction(int factionIndex, out string reason)
        {
            FactionSlot slot = FactionSlot.Resolve(factionIndex);
            reason = slot.IsValid && !slot.IsNeutral ? "" : "A playable faction is required.";
            return reason.Length == 0;
        }

        public RtsFactory CreateFactoryAtPlacement(Vector3 localPos, float yaw, string presetName, int factionIndex)
        {
            if (!CanUseFactoryFaction(factionIndex, out _)) return null;
            if(config?.settings==null||!config.settings.enableFactories||activeFactories.Count(f=>f.factionId==factionIndex)>=Math.Max(1,config.settings.maxFactoriesPerFaction))return null;
            FactoryPreset preset = config.factoryPresets.FirstOrDefault(p => string.Equals(p.presetName, presetName, StringComparison.OrdinalIgnoreCase));
            if(preset==null)return null;
            GlobalPosition globalPos=localPos.ToGlobalPosition();
            var factory = new RtsFactory
            {
                id = Guid.NewGuid().ToString("N"), displayName = preset.presetName, presetName = preset.presetName,
                factionId = factionIndex, factionName = FactionSlot.Resolve(factionIndex).DisplayName,
                factoryType = ParseType(preset.type), globalX = globalPos.x, globalY = globalPos.y, globalZ = globalPos.z,
                yaw = yaw, enabled = true, generateIncome = true, incomePerMinute = preset.incomePerMinute,
                produceUnits = preset.produceUnits, consumeBudgetForProduction = true,
                productionUnitKeys = new List<string>(preset.productionUnitKeys ?? new List<string>()),
                productionIntervalSeconds = Math.Max(1f, preset.productionIntervalSeconds),
                maxActiveProducedUnits = Math.Max(1, preset.maxActiveProducedUnits), spawnRadius = 50f,
                isVirtual = true, visualBuilding = preset.visualBuilding ?? ""
            };
            activeFactories.Add(factory);
            SaveInstances();
            return factory;
        }

        public void DeleteFactory(RtsFactory factory) { if (factory != null && activeFactories.Remove(factory)) SaveInstances(); }
        public void SetFactoryEnabled(RtsFactory factory, bool enabled) { if (factory != null) { factory.enabled = enabled; SaveInstances(); } }
        public void SetFactoryProductionEnabled(RtsFactory factory, bool enabled) { if (factory != null) { factory.produceUnits = enabled; SaveInstances(); } }
        public void SetFactoryConsumesBudget(RtsFactory factory, bool consumes) { if (factory != null) { factory.consumeBudgetForProduction = consumes; SaveInstances(); } }
        public void StartAllFactories() { foreach (RtsFactory f in activeFactories) f.enabled = true; SaveInstances(); }
        public void StopAllFactories() { foreach (RtsFactory f in activeFactories) f.enabled = false; SaveInstances(); }
        public void AddUnitToProductionQueue(RtsFactory factory, UnitDefinition definition) { if (factory != null && definition != null && !string.IsNullOrEmpty(definition.jsonKey)&&CanQueueDefinition(factory,definition)&&factory.productionUnitKeys.Count<HorusMod.Shared.HorusProtocol.MaxEntitiesPerCommand) { factory.productionUnitKeys.Add(definition.jsonKey); SaveInstances(); } }
        public void RemoveProductionQueueItem(RtsFactory factory, int index) { if (factory != null && index >= 0 && index < factory.productionUnitKeys.Count) { factory.productionUnitKeys.RemoveAt(index); SaveInstances(); } }
        public void ClearProductionQueue(RtsFactory factory) { if (factory != null) { factory.productionUnitKeys.Clear(); factory.currentProductionIndex = 0; SaveInstances(); } }
        public void SetRallyPoint(RtsFactory factory, Vector3 point) { if (factory != null) { factory.useRallyPoint = true; factory.rallyX = point.x; factory.rallyY = point.y; factory.rallyZ = point.z; SaveInstances(); } }
        public void ClearRallyPoint(RtsFactory factory) { if (factory != null) { factory.useRallyPoint = false; SaveInstances(); } }
        public void ReloadConfig() { LoadFactoryConfig();LoadInstances(); }
        public void LoadOrCreateConfig() { LoadFactoryConfig();LoadInstances(); }
        public void ResetPresetsToDefaults() { config=CreateDefaultConfig();SaveFactoryConfig(); }

        public bool CanQueueDefinition(RtsFactory factory,UnitDefinition definition)
        {
            if(factory==null||definition==null)return false;
            UnitEntry entry=UnitCatalog.FindByDefinition(definition);
            if(entry?.IsLiveOrdnance==true||definition is MissileDefinition)return false;
            if(factory.factoryType==RtsFactoryType.MixedProduction)return true;
            if(factory.factoryType==RtsFactoryType.Economy)return false;
            SpawnKind kind=entry?.SpawnKind??SpawnKind.Other;
            switch(factory.factoryType){case RtsFactoryType.GroundProduction:case RtsFactoryType.DefenseProduction:return kind==SpawnKind.Vehicle||kind==SpawnKind.Building||kind==SpawnKind.Scenery;case RtsFactoryType.AirProduction:return kind==SpawnKind.Aircraft;case RtsFactoryType.NavalProduction:return kind==SpawnKind.Ship;default:return false;}
        }

        private void LoadFactoryConfig()
        {
            try{Directory.CreateDirectory(Path.GetDirectoryName(configPath));if(!File.Exists(configPath)){config=CreateDefaultConfig();SaveFactoryConfig();return;}config=JsonConvert.DeserializeObject<RtsFactoriesConfig>(File.ReadAllText(configPath));if(config?.settings==null||config.factoryPresets==null||config.factoryPresets.Count==0)throw new InvalidDataException("Factory config is incomplete.");}
            catch(Exception ex){HorusLog.Warning("Factory","Failed to load server factory config: "+ex.Message);config=CreateDefaultConfig();SaveFactoryConfig();}
        }
        private void SaveFactoryConfig(){try{Directory.CreateDirectory(Path.GetDirectoryName(configPath));File.WriteAllText(configPath,JsonConvert.SerializeObject(config,Formatting.Indented));}catch(Exception ex){HorusLog.Warning("Factory","Failed to save server factory config: "+ex.Message);}}

        public void SaveInstances()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(instancesPath));
                var list = new SerializableFactoryList();
                foreach (RtsFactory factory in activeFactories) list.factories.Add(ToSerializable(factory));
                File.WriteAllText(instancesPath, JsonConvert.SerializeObject(list, Formatting.Indented));
            }
            catch (Exception ex) { HorusLog.Warning("Factory", "Failed to save server factories: " + ex.Message); }
        }

        public void LoadInstances()
        {
            try
            {
                if (!File.Exists(instancesPath)) return;
                SerializableFactoryList saved = JsonConvert.DeserializeObject<SerializableFactoryList>(File.ReadAllText(instancesPath));
                activeFactories.Clear();
                if (saved?.factories == null) return;
                foreach (SerializableFactory value in saved.factories) activeFactories.Add(FromSerializable(value));
            }
            catch (Exception ex) { HorusLog.Warning("Factory", "Failed to load server factories: " + ex.Message); }
        }

        private static RtsFactoriesConfig CreateDefaultConfig() => new RtsFactoriesConfig
        {
            version = 2,
            settings = new RtsFactoriesSettings(),
            factoryPresets = new List<FactoryPreset>
            {
                new FactoryPreset { presetName = "Economy Outpost", type = "Economy", incomePerMinute = 300f, produceUnits = false, productionIntervalSeconds = 0f, maxActiveProducedUnits = 0, productionUnitKeys = new List<string>(), visualBuilding = "Storage Tank" },
                new FactoryPreset { presetName = "Ground Vehicle Factory", type = "GroundProduction", incomePerMinute = 100f, produceUnits = true, productionIntervalSeconds = 90f, maxActiveProducedUnits = 10, productionUnitKeys = new List<string> { "Nailer" }, visualBuilding = "Large Factory" },
                new FactoryPreset { presetName = "Airbase Production", type = "AirProduction", incomePerMinute = 150f, produceUnits = true, productionIntervalSeconds = 120f, maxActiveProducedUnits = 6, productionUnitKeys = new List<string> { "Cricket" }, visualBuilding = "Medium Aircraft Hangar" },
                new FactoryPreset { presetName = "Naval Yard", type = "NavalProduction", incomePerMinute = 200f, produceUnits = true, productionIntervalSeconds = 180f, maxActiveProducedUnits = 4, productionUnitKeys = new List<string> { "Goldfinch" }, visualBuilding = "Large Factory" },
                new FactoryPreset { presetName = "Defense Battery", type = "DefenseProduction", incomePerMinute = 50f, produceUnits = true, productionIntervalSeconds = 100f, maxActiveProducedUnits = 8, productionUnitKeys = new List<string> { "23mm AAA Emplacement", "IRM-S1 Emplacement", "AT-145 Emplacement", "Guard Tower", "Pillbox", "Radar Station" }, visualBuilding = "Radar Station" },
                new FactoryPreset { presetName = "Mixed Production", type = "MixedProduction", incomePerMinute = 75f, produceUnits = true, productionIntervalSeconds = 110f, maxActiveProducedUnits = 8, productionUnitKeys = new List<string> { "Nailer", "Cricket", "Goldfinch", "23mm AAA Emplacement", "IRM-S1 Emplacement", "Guard Tower" }, visualBuilding = "Large Factory" }
            }
        };

        private static RtsFactoryType ParseType(string value) => Enum.TryParse(value, true, out RtsFactoryType parsed) ? parsed : RtsFactoryType.GroundProduction;
        private static SerializableFactory ToSerializable(RtsFactory f) => new SerializableFactory
        {
            id=f.id, displayName=f.displayName, presetName=f.presetName, factionId=f.factionId, factionName=f.factionName,
            factoryType=f.factoryType.ToString(), globalX=f.globalX, globalY=f.globalY, globalZ=f.globalZ, yaw=f.yaw,
            enabled=f.enabled, generateIncome=f.generateIncome, incomePerMinute=f.incomePerMinute, produceUnits=f.produceUnits,
            consumeBudgetForProduction=f.consumeBudgetForProduction, productionUnitKeys=new List<string>(f.productionUnitKeys),
            currentProductionIndex=f.currentProductionIndex, productionIntervalSeconds=f.productionIntervalSeconds,
            productionTimer=f.productionTimer, maxActiveProducedUnits=f.maxActiveProducedUnits, useRallyPoint=f.useRallyPoint,
            rallyX=f.rallyX, rallyY=f.rallyY, rallyZ=f.rallyZ, spawnRadius=f.spawnRadius, anchorUnitName=f.anchorUnitName,
            anchorDestroyed=f.anchorDestroyed, isVirtual=f.isVirtual, visualBuilding=f.visualBuilding
        };
        private static RtsFactory FromSerializable(SerializableFactory f) => new RtsFactory
        {
            id=f.id, displayName=f.displayName, presetName=f.presetName, factionId=f.factionId, factionName=f.factionName,
            factoryType=ParseType(f.factoryType), globalX=f.globalX, globalY=f.globalY, globalZ=f.globalZ, yaw=f.yaw,
            enabled=f.enabled, generateIncome=f.generateIncome, incomePerMinute=f.incomePerMinute, produceUnits=f.produceUnits,
            consumeBudgetForProduction=f.consumeBudgetForProduction, productionUnitKeys=f.productionUnitKeys ?? new List<string>(),
            currentProductionIndex=f.currentProductionIndex, productionIntervalSeconds=f.productionIntervalSeconds,
            productionTimer=f.productionTimer, maxActiveProducedUnits=f.maxActiveProducedUnits, useRallyPoint=f.useRallyPoint,
            rallyX=f.rallyX, rallyY=f.rallyY, rallyZ=f.rallyZ, spawnRadius=f.spawnRadius, anchorUnitName=f.anchorUnitName,
            anchorDestroyed=f.anchorDestroyed, isVirtual=f.isVirtual, visualBuilding=f.visualBuilding
        };
    }
}
