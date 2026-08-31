using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HorusMod.Data;
using HorusMod.Logging;
using HorusMod.Shared;
using HorusMod.Spawning;
using Mirage;
using Newtonsoft.Json;
using UnityEngine;

namespace HorusMod.Economy
{
    /// <summary>Headless factory runtime. It deliberately has no HorusManager or UI dependency.</summary>
    public sealed class RtsFactoryManager
    {
        public static RtsFactoryManager Instance { get; private set; }
        public static Action<Unit> UnitProduced { get; set; }
        public static Action<Unit> UnitRemoved { get; set; }
        public readonly List<RtsFactory> activeFactories = new List<RtsFactory>();

        private readonly string instancesPath = Path.Combine(Paths.ConfigPath, "HorusMod", "rts_factories_server.json");
        private readonly string configPath = Path.Combine(Paths.ConfigPath, "HorusMod", "rts_factories_server_config.json");
        private RtsFactoriesConfig config;
        private float incomeAccumulator;

        public RtsFactoriesConfig Config => config;
        public bool LastPersistenceSucceeded { get; private set; } = true;
        public string LastPersistenceMessage { get; private set; } = "Not attempted.";

        public RtsFactoryManager()
        {
            Instance = this;
            LoadFactoryConfig();
        }

        public void InitializeMatchFactories() => LoadInstances();
        public void ResetMatchFactories(){foreach(RtsFactory factory in activeFactories)DestroyFactoryAnchor(factory);activeFactories.Clear();}

        public void Tick()
        {
            RtsEconomyManager economy=RtsEconomyManager.Instance;
            if (economy?.CurrentMode != HorusMode.RtsCommander||config?.settings==null||!config.settings.enableFactories) return;
            foreach(RtsFactory factory in activeFactories)EnsureFactoryAnchor(factory);
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
            UnitEntry entry = FindUniqueEntry(key);
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
            if (!CanUseFactoryFaction(factionIndex, out _) || !HorusPersistencePolicy.IsSafePosition(localPos.x,localPos.y,localPos.z) || !HorusPersistencePolicy.IsFinite(yaw)) return null;
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
            if(!HorusPersistencePolicy.IsSafePosition(factory.globalX,factory.globalY,factory.globalZ)||
               !HorusFactoryPolicy.IsValidRuntimeNumbers(factory.yaw,factory.incomePerMinute,factory.productionIntervalSeconds,0f,factory.maxActiveProducedUnits,factory.spawnRadius,factory.produceUnits))return null;
            if(!HorusMod.Shared.HorusPersistencePolicy.IsSafeStringCollection(factory.productionUnitKeys,HorusMod.Shared.HorusProtocol.MaxEntitiesPerCommand,out _))return null;
            foreach(string key in factory.productionUnitKeys){UnitEntry entry=FindUniqueEntry(key);if(entry?.Def==null||!CanQueueDefinition(factory,entry.Def))return null;}
            if(!string.IsNullOrWhiteSpace(factory.visualBuilding))
            {
                Unit anchor=SpawnFactoryVisual(factory);if(anchor==null)return null;factory.anchorUnit=anchor;factory.anchorUnitName=anchor.unitName;factory.anchorDestroyed=false;UnitProduced?.Invoke(anchor);
            }
            activeFactories.Add(factory);
            SaveInstances();
            return factory;
        }

        public void DeleteFactory(RtsFactory factory)
        {
            if(factory==null||!activeFactories.Remove(factory))return;
            DestroyFactoryAnchor(factory);
            SaveInstances();
        }

        private static void DestroyFactoryAnchor(RtsFactory factory)
        {
            Unit anchor=factory?.anchorUnit;if(factory!=null)factory.anchorUnit=null;
            if(factory==null||!factory.isVirtual||anchor==null)return;
            UnitRemoved?.Invoke(anchor);try{if(anchor.Identity!=null)NetworkServer.Destroy(anchor.gameObject);else UnityEngine.Object.Destroy(anchor.gameObject);}catch(Exception ex){HorusLog.Warning("Factory","Failed to destroy dedicated factory visual: "+ex.Message);}
        }
        public void SetFactoryEnabled(RtsFactory factory, bool enabled) { if (factory != null) { factory.enabled = enabled&&!factory.anchorDestroyed; SaveInstances(); } }
        public void SetFactoryProductionEnabled(RtsFactory factory, bool enabled) { if (factory != null) { factory.produceUnits = enabled; SaveInstances(); } }
        public void SetFactoryConsumesBudget(RtsFactory factory, bool consumes) { if (factory != null) { factory.consumeBudgetForProduction = consumes; SaveInstances(); } }
        public void StartAllFactories() { foreach (RtsFactory f in activeFactories) f.enabled = !f.anchorDestroyed; SaveInstances(); }
        public void StopAllFactories() { foreach (RtsFactory f in activeFactories) f.enabled = false; SaveInstances(); }
        public void AddUnitToProductionQueue(RtsFactory factory, UnitDefinition definition) { if (factory != null && definition != null && !string.IsNullOrEmpty(definition.jsonKey)&&FindUniqueEntry(definition.jsonKey)?.Def==definition&&CanQueueDefinition(factory,definition)&&CanAppendProductionKey(factory,definition.jsonKey)) { factory.productionUnitKeys.Add(definition.jsonKey); SaveInstances(); } }
        public void RemoveProductionQueueItem(RtsFactory factory, int index) { if (factory != null && index >= 0 && index < factory.productionUnitKeys.Count) { factory.productionUnitKeys.RemoveAt(index); SaveInstances(); } }
        public void ClearProductionQueue(RtsFactory factory) { if (factory != null) { factory.productionUnitKeys.Clear(); factory.currentProductionIndex = 0; SaveInstances(); } }
        public void SetRallyPoint(RtsFactory factory, Vector3 point) { if (factory != null && HorusPersistencePolicy.IsSafePosition(point.x,point.y,point.z)) { factory.useRallyPoint = true; factory.rallyX = point.x; factory.rallyY = point.y; factory.rallyZ = point.z; SaveInstances(); } }
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

        private static bool CanAppendProductionKey(RtsFactory factory,string key)
        {
            if(factory?.productionUnitKeys==null||factory.productionUnitKeys.Count>=HorusMod.Shared.HorusProtocol.MaxEntitiesPerCommand||!HorusMod.Shared.HorusWireText.IsStableKey(key))return false;
            int total=System.Text.Encoding.UTF8.GetByteCount(key);foreach(string existing in factory.productionUnitKeys){if(!HorusMod.Shared.HorusWireText.IsStableKey(existing))return false;total+=System.Text.Encoding.UTF8.GetByteCount(existing);if(total>HorusMod.Shared.HorusProtocol.MaxStringListBytes)return false;}return total<=HorusMod.Shared.HorusProtocol.MaxStringListBytes;
        }

        private static UnitEntry FindUniqueEntry(string key)
        {
            IReadOnlyList<UnitEntry> matches=UnitCatalog.FindAll(key);return matches.Count==1?matches[0]:null;
        }

        private void EnsureFactoryAnchor(RtsFactory factory)
        {
            if(factory==null||!factory.isVirtual||string.IsNullOrWhiteSpace(factory.visualBuilding)||factory.anchorDestroyed)return;
            if(factory.anchorUnit!=null)
            {
                if(!factory.anchorUnit.Networkdisabled)return;UnitRemoved?.Invoke(factory.anchorUnit);factory.anchorUnit=null;factory.anchorDestroyed=true;factory.enabled=false;factory.lastStatus="Factory visual was destroyed";SaveInstances();return;
            }
            if(factory.anchorUnit==null&&!string.IsNullOrEmpty(factory.anchorUnitName))
            {
                factory.anchorDestroyed=true;factory.enabled=false;factory.lastStatus="Factory visual was destroyed";SaveInstances();return;
            }
            if(Time.unscaledTime<factory.nextAnchorRetryTime)return;
            Unit anchor=SpawnFactoryVisual(factory);
            if(anchor!=null)
            {
                factory.anchorUnit=anchor;factory.anchorUnitName=anchor.unitName;factory.anchorSpawnFailures=0;factory.nextAnchorRetryTime=0f;factory.lastStatus="Ready";UnitProduced?.Invoke(anchor);SaveInstances();return;
            }
            factory.anchorSpawnFailures++;float delay=Mathf.Min(60f,5f*Mathf.Pow(2f,Mathf.Min(4,factory.anchorSpawnFailures-1)));factory.nextAnchorRetryTime=Time.unscaledTime+delay;factory.lastStatus="Visual spawn failed; retry in "+delay.ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+"s";
        }

        private Unit SpawnFactoryVisual(RtsFactory factory)
        {
            UnitEntry entry=ResolveVisualEntry(factory.visualBuilding);
            FactionSlot faction=FactionSlot.Resolve(factory.factionId);
            if(entry?.Def==null||entry.IsLookupOnly||entry.SpawnKind!=SpawnKind.Building||!faction.IsValid)return null;
            var request=new HorusSpawnRequest{Definition=entry.Def,Position=new GlobalPosition(factory.globalX,factory.globalY,factory.globalZ),Rotation=Quaternion.Euler(0f,factory.yaw,0f),HQ=faction.HQ,Surface=entry.PlacementSurface,UniqueName=HorusMod.Shared.HorusWireText.Clamp((entry.Def.jsonKey??"factory")+"_factory_"+factory.id)};
            HorusSpawnResult spawned=HorusSpawnService.Spawn(request);if(!spawned.Success){HorusLog.Warning("Factory","Dedicated factory visual spawn failed: "+spawned.Message);return null;}return spawned.Unit;
        }

        private static UnitEntry ResolveVisualEntry(string requested)
        {
            UnitCatalog.EnsureBuilt(MissionManager.AllowEventContent);string name=ResolveVisualAlias(requested);List<UnitEntry> matches=UnitCatalog.Entries.Where(entry=>entry?.Def!=null&&entry.SpawnKind==SpawnKind.Building&&(string.Equals(entry.Def.jsonKey,name,StringComparison.OrdinalIgnoreCase)||string.Equals(entry.Def.unitName,name,StringComparison.OrdinalIgnoreCase))).ToList();return matches.Count==1?matches[0]:null;
        }

        private static string ResolveVisualAlias(string name)
        {
            if(string.Equals(name,"Solar Array",StringComparison.OrdinalIgnoreCase))return "Storage Tank";
            if(string.Equals(name,"Vehicle Factory",StringComparison.OrdinalIgnoreCase))return "Large Factory";
            if(string.Equals(name,"Hangar",StringComparison.OrdinalIgnoreCase))return "Medium Aircraft Hangar";
            if(string.Equals(name,"Warehouse",StringComparison.OrdinalIgnoreCase))return "Large Factory";
            return name;
        }

        private void LoadFactoryConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                if(!File.Exists(configPath)){config=CreateDefaultConfig();SaveFactoryConfig();return;}
                long length=new FileInfo(configPath).Length;if(length<0||length>HorusEconomyPolicy.MaxConfigFileBytes)throw new InvalidDataException("Factory config file is oversized.");
                RtsFactoriesConfig loaded=JsonConvert.DeserializeObject<RtsFactoriesConfig>(File.ReadAllText(configPath),new JsonSerializerSettings{MaxDepth=32});
                ValidateFactoryConfig(loaded);config=loaded;
            }
            catch(Exception ex){HorusLog.Warning("Factory","Failed to load server factory config: "+ex.Message);config=CreateDefaultConfig();SaveFactoryConfig();}
        }

        private static void ValidateFactoryConfig(RtsFactoriesConfig value)
        {
            if(value==null||value.version<1||value.version>2||value.settings==null||value.factoryPresets==null||value.factoryPresets.Count<1||value.factoryPresets.Count>HorusFactoryPolicy.MaxPresets)throw new InvalidDataException("Factory config is incomplete or unsupported.");
            if(!HorusFactoryPolicy.IsValidFactoryLimit(value.settings.maxFactoriesPerFaction))throw new InvalidDataException("Factory limit is out of range.");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(FactoryPreset preset in value.factoryPresets)
            {
                if(preset==null||string.IsNullOrWhiteSpace(preset.presetName)||!HorusWireText.IsStableKey(preset.presetName)||!names.Add(preset.presetName))throw new InvalidDataException("Factory preset name is invalid or duplicated.");
                if(!Enum.TryParse(preset.type,true,out RtsFactoryType type)||!Enum.IsDefined(typeof(RtsFactoryType),type))throw new InvalidDataException("Factory preset type is invalid.");
                if(!HorusFactoryPolicy.IsValidIncome(preset.incomePerMinute)||!HorusFactoryPolicy.IsValidProduction(preset.productionIntervalSeconds,preset.maxActiveProducedUnits,preset.produceUnits))throw new InvalidDataException("Factory preset numeric state is invalid.");
                if(!HorusPersistencePolicy.IsSafeStringCollection(preset.productionUnitKeys,HorusProtocol.MaxEntitiesPerCommand,out _))throw new InvalidDataException("Factory preset queue is invalid or oversized.");
                if(string.IsNullOrWhiteSpace(preset.visualBuilding)||!HorusWireText.IsStableKey(preset.visualBuilding))throw new InvalidDataException("Factory visual building key is invalid.");
            }
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
                LastPersistenceSucceeded=true;LastPersistenceMessage="Factory state saved.";
            }
            catch (Exception ex) { LastPersistenceSucceeded=false;LastPersistenceMessage="Factory save failed: "+ex.Message;HorusLog.Warning("Factory", "Failed to save server factories: " + ex.Message); }
        }

        public void LoadInstances()
        {
            try
            {
                if (!File.Exists(instancesPath)){LastPersistenceSucceeded=true;LastPersistenceMessage="No persisted factory state exists.";return;}
                long length=new FileInfo(instancesPath).Length;if(length<0||length>HorusEconomyPolicy.MaxConfigFileBytes)throw new InvalidDataException("Factory state file is oversized.");
                SerializableFactoryList saved = JsonConvert.DeserializeObject<SerializableFactoryList>(File.ReadAllText(instancesPath),new JsonSerializerSettings{MaxDepth=32});
                if(saved?.factories==null||saved.factories.Count>HorusEconomyPolicy.MaxConfigEntries)throw new InvalidDataException("Factory state has no valid bounded factory collection.");
                var restored=new List<RtsFactory>();var ids=new HashSet<string>(StringComparer.Ordinal);
                var factionCounts=new Dictionary<int,int>();
                foreach(SerializableFactory value in saved.factories)
                {
                    RtsFactory factory=RestoreValidated(value,ids);
                    int count=factionCounts.TryGetValue(factory.factionId,out int existing)?existing+1:1;
                    if(count>Math.Max(1,config.settings.maxFactoriesPerFaction))throw new InvalidDataException("Persisted faction factory limit exceeded.");
                    factionCounts[factory.factionId]=count;restored.Add(factory);
                }
                foreach(RtsFactory active in activeFactories)DestroyFactoryAnchor(active);activeFactories.Clear();activeFactories.AddRange(restored);
                LastPersistenceSucceeded=true;LastPersistenceMessage="Factory state loaded.";
            }
            catch (Exception ex) { LastPersistenceSucceeded=false;LastPersistenceMessage="Factory load rejected: "+ex.Message;HorusLog.Warning("Factory", "Failed to load server factories: " + ex.Message); }
        }

        private RtsFactory RestoreValidated(SerializableFactory value,HashSet<string> ids)
        {
            if(value==null)throw new InvalidDataException("Persisted factory entry is null.");
            if(string.IsNullOrWhiteSpace(value.id)||!HorusMod.Shared.HorusWireText.IsStableKey(value.id)||!ids.Add(value.id))throw new InvalidDataException("Persisted factory id is invalid or duplicated.");
            if(!CanUseFactoryFaction(value.factionId,out _))throw new InvalidDataException("Persisted factory faction is invalid.");
            if(!HorusMod.Shared.HorusPersistencePolicy.IsSafePosition(value.globalX,value.globalY,value.globalZ)||!HorusMod.Shared.HorusPersistencePolicy.IsSafePosition(value.rallyX,value.rallyY,value.rallyZ))throw new InvalidDataException("Persisted factory position is invalid.");
            if(!HorusFactoryPolicy.IsValidRuntimeNumbers(value.yaw,value.incomePerMinute,value.productionIntervalSeconds,value.productionTimer,value.maxActiveProducedUnits,value.spawnRadius,value.produceUnits))throw new InvalidDataException("Persisted factory numeric state is invalid or out of range.");
            FactoryPreset preset=config.factoryPresets.FirstOrDefault(item=>string.Equals(item.presetName,value.presetName??value.displayName,StringComparison.OrdinalIgnoreCase));
            if(preset==null)throw new InvalidDataException("Persisted factory preset is unknown.");
            List<string> keys=value.productionUnitKeys??new List<string>();
            if(!HorusMod.Shared.HorusPersistencePolicy.IsSafeStringCollection(keys,HorusMod.Shared.HorusProtocol.MaxEntitiesPerCommand,out _))throw new InvalidDataException("Persisted factory queue is invalid or oversized.");
            RtsFactory restored=FromSerializable(value);restored.factoryType=ParseType(preset.type);restored.presetName=preset.presetName;restored.displayName=preset.presetName;restored.visualBuilding=preset.visualBuilding??"";restored.lastStatus=HorusMod.Shared.HorusWireText.SanitizeVisible(value.lastStatus);
            foreach(string key in keys)
            {
                UnitEntry entry=FindUniqueEntry(key);if(entry?.Def==null||!CanQueueDefinition(restored,entry.Def))throw new InvalidDataException("Persisted factory queue contains an incompatible or ambiguous definition.");
            }
            restored.currentProductionIndex=keys.Count==0?0:Math.Max(0,Math.Min(value.currentProductionIndex,keys.Count-1));restored.factionName=FactionSlot.Resolve(value.factionId).DisplayName;restored.anchorUnit=null;restored.anchorUnitName="";restored.anchorDestroyed=false;restored.isVirtual=true;
            return restored;
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
            anchorDestroyed=f.anchorDestroyed, isVirtual=f.isVirtual, visualBuilding=f.visualBuilding,lastStatus=f.lastStatus
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
            anchorDestroyed=f.anchorDestroyed, isVirtual=f.isVirtual, visualBuilding=f.visualBuilding,lastStatus=f.lastStatus
        };
    }
}
