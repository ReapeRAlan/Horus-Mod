using System;
using System.Collections.Generic;
using System.Linq;
using HorusMod.Data;
using HorusMod.Economy;
using HorusMod.Interaction;
using HorusMod.Loadouts;
using HorusMod.Shared;
using HorusMod.Spawning;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace HorusMod.Server
{
    internal sealed class HorusServerCommandExecutor
    {
        private readonly HorusServerState state;
        private readonly HorusOrders orders;
        private readonly bool allowMissionUnitDelete;
        private readonly Stack<JournalEntry> undo = new Stack<JournalEntry>();
        private readonly Stack<JournalEntry> redo = new Stack<JournalEntry>();
        private bool replaying;

        private sealed class JournalEntry
        {
            public Action Undo;
            public Action Redo;
        }

        private sealed class UnitSnapshot
        {
            public UnitDefinition Definition;
            public GlobalPosition Position;
            public Quaternion Rotation;
            public FactionHQ HQ;
            public float Skill;
            public float Bravery;
            public float Fuel;
            public int Livery;
            public Loadout Loadout;
            public uint CurrentId;
        }

        public HorusServerCommandExecutor(HorusServerState state, MonoBehaviour runner, bool allowMissionUnitDelete)
        {
            this.state = state;
            this.allowMissionUnitDelete = allowMissionUnitDelete;
            orders = new HorusOrders(runner);
            if (RtsEconomyManager.Instance == null) new RtsEconomyManager();
            RtsFactoryManager.UnitProduced = unit => { if(unit!=null)state.RecordSpawn(unit.persistentID.Id); };
        }

        public void ResetMission()
        {
            undo.Clear();redo.Clear();replaying=false;
            RtsEconomyManager.Instance?.ResetRuntimeState();
        }

        public void Tick()
        {
            orders.Tick();
            RtsEconomyManager.Instance?.Tick();
        }

        public HorusCommandResult Execute(HorusCommandEnvelope command)
        {
            var result = NewResult(command);
            try
            {
                switch (command.Command)
                {
                    case HorusCommandKind.Spawn: Spawn(command, result); break;
                    case HorusCommandKind.Duplicate: Duplicate(command, result); break;
                    case HorusCommandKind.Delete: Delete(command, result); break;
                    case HorusCommandKind.Move: Move(command, result); break;
                    case HorusCommandKind.Hold: Hold(command, result); break;
                    case HorusCommandKind.ClearOrders: ClearOrders(command, result); break;
                    case HorusCommandKind.AttackTarget: AttackTarget(command, result); break;
                    case HorusCommandKind.AttackMove: AttackMove(command, result); break;
                    case HorusCommandKind.Patrol: Patrol(command, result); break;
                    case HorusCommandKind.Guard: Guard(command, result); break;
                    case HorusCommandKind.SetRulesOfEngagement: SetRules(command, result); break;
                    case HorusCommandKind.SetLoadout: SetLoadout(command, result); break;
                    case HorusCommandKind.SetLivery: SetLivery(command, result); break;
                    case HorusCommandKind.SetSkill: SetSkill(command, result); break;
                    case HorusCommandKind.SetFuel: SetFuel(command, result); break;
                    case HorusCommandKind.SetBudget: SetBudget(command, result, false); break;
                    case HorusCommandKind.AdjustBudget: SetBudget(command, result, true); break;
                    case HorusCommandKind.CreateFactory: CreateFactory(command, result); break;
                    case HorusCommandKind.DeleteFactory: Factory(command, result, FactoryAction.Delete); break;
                    case HorusCommandKind.SetFactoryEnabled: Factory(command, result, FactoryAction.Enable); break;
                    case HorusCommandKind.SetFactoryProductionEnabled: Factory(command, result, FactoryAction.Production); break;
                    case HorusCommandKind.SetFactoryConsumesBudget: Factory(command, result, FactoryAction.Consumes); break;
                    case HorusCommandKind.QueueFactoryUnit: Factory(command, result, FactoryAction.Queue); break;
                    case HorusCommandKind.RemoveFactoryQueueItem: Factory(command, result, FactoryAction.RemoveQueue); break;
                    case HorusCommandKind.ClearFactoryQueue: Factory(command, result, FactoryAction.ClearQueue); break;
                    case HorusCommandKind.SetFactoryRally: Factory(command, result, FactoryAction.SetRally); break;
                    case HorusCommandKind.ClearFactoryRally: Factory(command, result, FactoryAction.ClearRally); break;
                    case HorusCommandKind.StartAllFactories: RtsFactoryManager.Instance.StartAllFactories(); Accept(result, "Factories started."); break;
                    case HorusCommandKind.StopAllFactories: RtsFactoryManager.Instance.StopAllFactories(); Accept(result, "Factories stopped."); break;
                    case HorusCommandKind.ReloadFactories: RtsFactoryManager.Instance.ReloadConfig(); Accept(result, "Factories reloaded."); break;
                    case HorusCommandKind.ResetFactoryPresets: RtsFactoryManager.Instance.ResetPresetsToDefaults(); Accept(result, "Factory presets reset."); break;
                    case HorusCommandKind.SaveFactories: RtsFactoryManager.Instance.SaveInstances(); Accept(result, "Factories saved."); break;
                    case HorusCommandKind.LoadFactories: RtsFactoryManager.Instance.LoadInstances(); Accept(result, "Factories loaded."); break;
                    case HorusCommandKind.Undo: Replay(undo, redo, true, result, "Undo complete."); break;
                    case HorusCommandKind.Redo: Replay(redo, undo, false, result, "Redo complete."); break;
                    case HorusCommandKind.SetRtsMode: SetRtsMode(command,result); break;
                    case HorusCommandKind.SetRtsDeployMode: SetRtsDeployMode(command,result);break;
                    case HorusCommandKind.AdjustUnitCap: AdjustUnitCap(command,result);break;
                    default: Reject(result, HorusResultCode.Unsupported, "Command is not implemented by this server."); break;
                }
            }
            catch (Exception ex)
            {
                Reject(result, HorusResultCode.InternalError, ex.GetType().Name + ": " + ex.Message);
            }
            if (result.Result == HorusResultCode.Accepted)
            {
                result.Revision = state.AdvanceRevision();
                if (!replaying && command.Command != HorusCommandKind.Undo && command.Command != HorusCommandKind.Redo) redo.Clear();
            }
            return result;
        }

        private void Spawn(HorusCommandEnvelope command, HorusCommandResult result)
        {
            HorusCommandPayload payload = command.Payload;
            if (payload.Points.Count != 1) { Reject(result, HorusResultCode.InvalidPayload, "Spawn requires one position."); return; }
            UnitEntry entry = ResolveCatalogEntry(payload.DefinitionKey,payload.SecondaryKey);
            if (entry?.Def == null) { Reject(result, HorusResultCode.NotFound, "Definition was not found."); return; }
            string requestAcknowledgementKey=null;
            if(entry.IsLookupOnly)
            {
                if(HorusMod.HorusPlugin.AllowIncompatibleContent?.Value!=true){Reject(result,HorusResultCode.PolicyDenied,"Lookup-only content is disabled by the server operator.");return;}
                HorusSpawnService.IssueIncompatibleContentAuthorization(entry.Key);requestAcknowledgementKey=entry.Key;
            }
            FactionSlot faction = FactionSlot.Resolve(payload.FactionIndex);
            if (!faction.IsValid) { Reject(result, HorusResultCode.InvalidPayload, "Faction index is invalid."); return; }
            HorusVector3 point = payload.Points[0];
            var request = new HorusSpawnRequest
            {
                Definition = entry.Def,
                Position = ResolveSpawnPosition(entry,ToGlobal(point)),
                Rotation = Quaternion.Euler(0f, payload.Yaw, 0f),
                HQ = faction.HQ,
                Surface = entry.PlacementSurface,
                UniqueName = string.IsNullOrWhiteSpace(payload.UniqueName) ? null : payload.UniqueName,
                Stationary = payload.BoolValue,
                Skill = Mathf.Clamp01(payload.FloatValue),
                MissileLaunchSpeed = payload.FloatValue2 > 0 ? payload.FloatValue2 : 250f,
                MissileLaunchElevation = payload.FloatValue3
            };
            request.IncompatibleContentAcknowledgementKey=requestAcknowledgementKey;
            Unit linkedTarget=null;
            if (payload.TargetUnitId != 0 && TryUnit(payload.TargetUnitId, out Unit target))
            {
                linkedTarget=target;
                request.TargetUnitName=target.UniqueName;
            }
            if (entry.Def.unitPrefab != null && entry.Def.unitPrefab.GetComponent<Aircraft>() != null)
            {
                var options = new AircraftSpawnOptions
                {
                    FuelRatio = payload.FloatValue2 > 0f ? Mathf.Clamp01(payload.FloatValue2) : 1f,
                    Livery = new LiveryKey(Math.Max(0, payload.IntValue)),
                    Skill = Mathf.Clamp01(payload.FloatValue),
                    Bravery = payload.FloatValue3 > 0f ? Mathf.Clamp01(payload.FloatValue3) : 0.575f
                };
                if (payload.MountKeys.Count > 0 && entry.Def is AircraftDefinition aircraftDefinition)
                {
                    var draft = new LoadoutDraft(aircraftDefinition.jsonKey, LoadoutSourceKind.CustomHardpoints,
                        payload.MountKeys, options.FuelRatio, payload.IntValue, "dedicated", "Dedicated request");
                    LoadoutApplyResult resolved = HorusLoadoutService.ResolveForSpawn(aircraftDefinition, faction.HQ, draft);
                    if (!resolved.Success) { Reject(result, HorusResultCode.InvalidPayload, resolved.Message); return; }
                    options.Loadout = resolved.ResolvedLoadout;
                }
                request.Aircraft = options;
            }
            RtsTransaction transaction = null;
            if (RtsEconomyManager.Instance.CurrentMode==HorusMode.RtsCommander)
            {
                transaction=RtsEconomyManager.Instance.CreateTransaction(entry.Def,payload.FactionIndex);
                if(!transaction.IsValid){Reject(result,HorusResultCode.PolicyDenied,transaction.DenialReason);return;}
            }
            HorusSpawnResult spawned = HorusSpawnService.Spawn(request);
            if (!spawned.Success) { Reject(result, HorusResultCode.NativeFailure, spawned.Message); return; }
            uint id = spawned.Unit.persistentID.Id;
            state.RecordSpawn(id);
            if(transaction!=null)RtsEconomyManager.Instance.CommitTransaction(transaction,spawned.Unit);
            if(entry.Supply?.CanResupplyShips==CapabilityState.Yes&&linkedTarget is Ship ship)
            {
                try{ship.RequestRearm();}
                catch(System.Exception ex){HorusMod.Logging.HorusLog.Warning("Supply","Dedicated ship rearm request failed: "+ex.Message);}
            }
            result.AffectedUnitIds.Add(id);
            Accept(result, "Spawned " + (entry.Def.unitName ?? entry.Def.jsonKey) + ".");
            if(!entry.IsLiveOrdnance)
            {
                UnitSnapshot snapshot=CaptureUnit(spawned.Unit);
                PushUndo(() => RemoveSnapshot(snapshot), () => RestoreSnapshot(snapshot));
            }
        }

        private void Duplicate(HorusCommandEnvelope command, HorusCommandResult result)
        {
            if (command.Payload.UnitIds.Count != 1 || !TryUnit(command.Payload.UnitIds[0], out Unit source)) { Reject(result, HorusResultCode.NotFound, "Source unit was not found."); return; }
            UnitEntry entry=UnitCatalog.FindByDefinition(source.definition);
            if(entry?.IsLiveOrdnance==true){Reject(result,HorusResultCode.PolicyDenied,"Live ordnance cannot be duplicated.");return;}
            UnitSnapshot snapshot=CaptureUnit(source);
            snapshot.Position=command.Payload.Points.Count==1?ResolveSpawnPosition(entry,ToGlobal(command.Payload.Points[0])):source.GlobalPosition()+new Vector3(20f,0f,20f);
            snapshot.CurrentId=0;
            Unit duplicate=RestoreSnapshot(snapshot);
            result.AffectedUnitIds.Add(duplicate.persistentID.Id);
            Accept(result,"Duplicated "+(source.definition?.unitName??"unit")+".");
            PushUndo(()=>RemoveSnapshot(snapshot),()=>RestoreSnapshot(snapshot));
        }

        private void Delete(HorusCommandEnvelope command, HorusCommandResult result)
        {
            if (command.Payload.UnitIds.Count == 0) { Reject(result, HorusResultCode.InvalidPayload, "Delete requires unit ids."); return; }
            int deleted = 0;
            var snapshots=new List<UnitSnapshot>();
            foreach (uint id in command.Payload.UnitIds)
            {
                if (!TryUnit(id, out Unit unit)) continue;
                if (!state.IsHorusOwned(id) && !allowMissionUnitDelete) continue;
                if(UnitCatalog.FindByDefinition(unit.definition)?.IsLiveOrdnance!=true)snapshots.Add(CaptureUnit(unit));
                DeleteUnit(unit); state.RecordDelete(id); result.AffectedUnitIds.Add(id); deleted++;
            }
            if (deleted == 0) { Reject(result, HorusResultCode.PolicyDenied, "No deletable Horus-owned units were supplied."); return; }
            Accept(result, "Deleted " + deleted + " unit(s).");
            if(snapshots.Count>0)PushUndo(()=>{foreach(UnitSnapshot snapshot in snapshots)RestoreSnapshot(snapshot);},()=>{foreach(UnitSnapshot snapshot in snapshots)RemoveSnapshot(snapshot);});
        }

        private void Move(HorusCommandEnvelope command, HorusCommandResult result)
        {
            if (command.Payload.Points.Count != 1) { Reject(result, HorusResultCode.InvalidPayload, "Move requires one destination."); return; }
            List<Unit> units = ResolveUnits(command.Payload.UnitIds);
            List<GlobalPosition> before=units.Select(unit=>unit.GlobalPosition()).ToList();
            GlobalPosition destination=ToGlobal(command.Payload.Points[0]);
            if (units.Count == 0 || !orders.IssueMove(units, ToGlobal(command.Payload.Points[0]), (HorusMod.Placement.FormationKind)Math.Max(0, command.Payload.IntValue)))
            { Reject(result, HorusResultCode.NativeFailure, "No unit accepted the move order."); return; }
            AddIds(result, units); Accept(result, "Move order accepted.");
            PushUndo(()=>ApplyDestinations(units,before),()=>orders.IssueMove(units,destination,(HorusMod.Placement.FormationKind)Math.Max(0,command.Payload.IntValue)));
        }

        private void Hold(HorusCommandEnvelope command, HorusCommandResult result) { List<Unit> units=ResolveUnits(command.Payload.UnitIds); if(units.Count==0){Reject(result,HorusResultCode.NotFound,"Units were not found.");return;} orders.SetHold(units,command.Payload.BoolValue);AddIds(result,units);Accept(result,"Hold state updated."); }
        private void ClearOrders(HorusCommandEnvelope command, HorusCommandResult result) { List<Unit> units=ResolveUnits(command.Payload.UnitIds); if(units.Count==0){Reject(result,HorusResultCode.NotFound,"Units were not found.");return;} orders.ClearOrders(units);AddIds(result,units);Accept(result,"Orders cleared."); }
        private void AttackTarget(HorusCommandEnvelope command, HorusCommandResult result) { List<Unit> units=ResolveUnits(command.Payload.UnitIds); if(!TryUnit(command.Payload.TargetUnitId,out Unit target)||!orders.IssueAttackTarget(units,target)){Reject(result,HorusResultCode.NativeFailure,"Attack target was rejected.");return;}AddIds(result,units);Accept(result,"Attack target accepted."); }
        private void AttackMove(HorusCommandEnvelope command, HorusCommandResult result) { List<Unit> units=ResolveUnits(command.Payload.UnitIds); if(command.Payload.Points.Count!=1||!orders.IssueAttackMove(units,ToGlobal(command.Payload.Points[0]))){Reject(result,HorusResultCode.NativeFailure,"Attack-move was rejected.");return;}AddIds(result,units);Accept(result,"Attack-move accepted."); }
        private void Patrol(HorusCommandEnvelope command, HorusCommandResult result) { List<Unit> units=ResolveUnits(command.Payload.UnitIds); var points=command.Payload.Points.Select(ToGlobal).ToList(); if(points.Count<2||!orders.IssuePatrol(units,points)){Reject(result,HorusResultCode.NativeFailure,"Patrol was rejected.");return;}AddIds(result,units);Accept(result,"Patrol accepted."); }
        private void Guard(HorusCommandEnvelope command, HorusCommandResult result) { List<Unit> units=ResolveUnits(command.Payload.UnitIds); if(!TryUnit(command.Payload.TargetUnitId,out Unit target)||!orders.IssueGuard(units,target)){Reject(result,HorusResultCode.NativeFailure,"Guard was rejected.");return;}AddIds(result,units);Accept(result,"Guard accepted."); }
        private void SetRules(HorusCommandEnvelope command, HorusCommandResult result) { if(!Enum.IsDefined(typeof(HorusRulesOfEngagement),command.Payload.IntValue)){Reject(result,HorusResultCode.InvalidPayload,"Rules of engagement value is invalid.");return;}List<Unit> units=ResolveUnits(command.Payload.UnitIds); if(units.Count==0){Reject(result,HorusResultCode.NotFound,"Units were not found.");return;} orders.SetRules(units,(HorusRulesOfEngagement)command.Payload.IntValue);AddIds(result,units);Accept(result,"Rules of engagement updated."); }

        private void SetLoadout(HorusCommandEnvelope command, HorusCommandResult result)
        {
            if (command.Payload.UnitIds.Count != 1 || !TryUnit(command.Payload.UnitIds[0], out Unit unit) || !(unit is Aircraft aircraft)) { Reject(result, HorusResultCode.NotFound, "Aircraft was not found."); return; }
            if (string.Equals(command.Payload.SecondaryKey,"standard",StringComparison.OrdinalIgnoreCase))
            {
                LoadoutApplyResult standard=HorusUnitEditor.TrySetLoadoutDetailed(aircraft,command.Payload.IntValue);
                if(!standard.Success){Reject(result,HorusResultCode.InvalidPayload,standard.Message);return;}
                result.AffectedUnitIds.Add(unit.persistentID.Id);Accept(result,"Standard loadout updated.");return;
            }
            var draft = new LoadoutDraft(aircraft.definition?.jsonKey, LoadoutSourceKind.CustomHardpoints, command.Payload.MountKeys,
                command.Payload.FloatValue > 0 ? command.Payload.FloatValue : aircraft.NetworkfuelLevel, command.Payload.IntValue, "dedicated", "Dedicated request");
            LoadoutApplyResult applied = HorusUnitEditor.TrySetLoadout(aircraft, draft);
            if (!applied.Success) { Reject(result, HorusResultCode.InvalidPayload, applied.Message); return; }
            result.AffectedUnitIds.Add(unit.persistentID.Id); Accept(result, "Loadout updated.");
        }
        private void SetLivery(HorusCommandEnvelope command, HorusCommandResult result) { if(command.Payload.UnitIds.Count!=1||!TryUnit(command.Payload.UnitIds[0],out Unit unit)||!(unit is Aircraft aircraft)||!HorusUnitEditor.TrySetLivery(aircraft,command.Payload.IntValue)){Reject(result,HorusResultCode.InvalidPayload,"Livery was rejected.");return;}result.AffectedUnitIds.Add(unit.persistentID.Id);Accept(result,"Livery updated."); }
        private void SetSkill(HorusCommandEnvelope command, HorusCommandResult result) { List<Unit> units=ResolveUnits(command.Payload.UnitIds);if(units.Count==0){Reject(result,HorusResultCode.NotFound,"Units were not found.");return;}foreach(Unit unit in units)HorusUnitEditor.SetSkill(unit,command.Payload.FloatValue);AddIds(result,units);Accept(result,"Skill updated."); }
        private void SetFuel(HorusCommandEnvelope command, HorusCommandResult result) { if(command.Payload.UnitIds.Count!=1||!TryUnit(command.Payload.UnitIds[0],out Unit unit)||!(unit is Aircraft aircraft)){Reject(result,HorusResultCode.NotFound,"Aircraft was not found.");return;}aircraft.NetworkfuelLevel=Mathf.Clamp01(command.Payload.FloatValue);result.AffectedUnitIds.Add(unit.persistentID.Id);Accept(result,"Fuel updated."); }
        private void SetBudget(HorusCommandEnvelope command, HorusCommandResult result, bool adjust) { if(!FactionSlot.Resolve(command.Payload.FactionIndex).IsValid){Reject(result,HorusResultCode.InvalidPayload,"Faction index is invalid.");return;}if(!adjust&&command.Payload.FloatValue<0f){Reject(result,HorusResultCode.InvalidPayload,"Budget cannot be negative.");return;}if(adjust)RtsEconomyManager.Instance.AdjustBudget(command.Payload.FactionIndex,command.Payload.FloatValue);else RtsEconomyManager.Instance.SetBudget(command.Payload.FactionIndex,command.Payload.FloatValue);Accept(result,adjust?"Budget adjusted.":"Budget set."); }
        private void SetRtsMode(HorusCommandEnvelope command,HorusCommandResult result){if(!Enum.IsDefined(typeof(HorusMode),command.Payload.IntValue)){Reject(result,HorusResultCode.InvalidPayload,"RTS mode is invalid.");return;}HorusMode mode=(HorusMode)command.Payload.IntValue;if(mode==HorusMode.RtsCommander){RtsEconomyManager.Instance.CurrentMode=mode;RtsEconomyManager.Instance.InitializeMatch();}else{RtsEconomyManager.Instance.ResetMatch();RtsEconomyManager.Instance.CurrentMode=mode;}Accept(result,"RTS mode updated.");}
        private void SetRtsDeployMode(HorusCommandEnvelope command,HorusCommandResult result){if(!Enum.IsDefined(typeof(RtsDeployMode),command.Payload.IntValue)){Reject(result,HorusResultCode.InvalidPayload,"RTS deployment mode is invalid.");return;}RtsEconomyManager.Instance.DeployMode=(RtsDeployMode)command.Payload.IntValue;Accept(result,"RTS deployment mode updated.");}
        private void AdjustUnitCap(HorusCommandEnvelope command,HorusCommandResult result){if(!FactionSlot.Resolve(command.Payload.FactionIndex).IsValid||command.Payload.IntValue==0||Math.Abs((long)command.Payload.IntValue)>1000){Reject(result,HorusResultCode.InvalidPayload,"Unit-cap adjustment is invalid.");return;}RtsEconomyManager.Instance.AdjustUnitCap(command.Payload.FactionIndex,command.Payload.IntValue);Accept(result,"RTS unit cap adjusted.");}

        private void CreateFactory(HorusCommandEnvelope command, HorusCommandResult result)
        {
            if(command.Payload.Points.Count!=1){Reject(result,HorusResultCode.InvalidPayload,"Factory requires one position.");return;}
            HorusVector3 point=command.Payload.Points[0];
            Vector3 local=ToGlobal(point).ToLocalPosition();
            RtsFactory factory=RtsFactoryManager.Instance.CreateFactoryAtPlacement(local,command.Payload.Yaw,command.Payload.PresetName,command.Payload.FactionIndex);
            if(factory==null){Reject(result,HorusResultCode.NativeFailure,"Factory could not be created.");return;}Accept(result,"Factory created: "+factory.id);
        }

        private enum FactoryAction { Delete, Enable, Production, Consumes, Queue, RemoveQueue, ClearQueue, SetRally, ClearRally }
        private void Factory(HorusCommandEnvelope command, HorusCommandResult result, FactoryAction action)
        {
            RtsFactory factory=RtsFactoryManager.Instance.activeFactories.FirstOrDefault(f=>string.Equals(f.id,command.Payload.FactoryId,StringComparison.Ordinal));
            if(factory==null){Reject(result,HorusResultCode.NotFound,"Factory was not found.");return;}
            switch(action)
            {
                case FactoryAction.Delete:RtsFactoryManager.Instance.DeleteFactory(factory);break;
                case FactoryAction.Enable:RtsFactoryManager.Instance.SetFactoryEnabled(factory,command.Payload.BoolValue);break;
                case FactoryAction.Production:RtsFactoryManager.Instance.SetFactoryProductionEnabled(factory,command.Payload.BoolValue);break;
                case FactoryAction.Consumes:RtsFactoryManager.Instance.SetFactoryConsumesBudget(factory,command.Payload.BoolValue);break;
                case FactoryAction.Queue:UnitEntry entry=UnitCatalog.Find(command.Payload.DefinitionKey);if(entry?.Def==null){Reject(result,HorusResultCode.NotFound,"Queue definition was not found.");return;}if(factory.productionUnitKeys.Count>=HorusProtocol.MaxEntitiesPerCommand||!RtsFactoryManager.Instance.CanQueueDefinition(factory,entry.Def)){Reject(result,HorusResultCode.PolicyDenied,"Definition is incompatible with this factory or its queue is full.");return;}RtsFactoryManager.Instance.AddUnitToProductionQueue(factory,entry.Def);break;
                case FactoryAction.RemoveQueue:if(command.Payload.IntValue<0||command.Payload.IntValue>=factory.productionUnitKeys.Count){Reject(result,HorusResultCode.InvalidPayload,"Factory queue index is invalid.");return;}RtsFactoryManager.Instance.RemoveProductionQueueItem(factory,command.Payload.IntValue);break;
                case FactoryAction.ClearQueue:RtsFactoryManager.Instance.ClearProductionQueue(factory);break;
                case FactoryAction.SetRally:if(command.Payload.Points.Count!=1){Reject(result,HorusResultCode.InvalidPayload,"Rally requires one position.");return;}HorusVector3 p=command.Payload.Points[0];RtsFactoryManager.Instance.SetRallyPoint(factory,new Vector3(p.X,p.Y,p.Z));break;
                case FactoryAction.ClearRally:RtsFactoryManager.Instance.ClearRallyPoint(factory);break;
            }
            Accept(result,"Factory updated.");
        }

        private void Replay(Stack<JournalEntry> source, Stack<JournalEntry> destination, bool undoDirection, HorusCommandResult result, string message)
        {
            if(source.Count==0){Reject(result,HorusResultCode.PolicyDenied,"Journal is empty.");return;}
            JournalEntry entry=source.Pop();replaying=true;try{(undoDirection?entry.Undo:entry.Redo)();destination.Push(entry);}finally{replaying=false;}Accept(result,message);
        }
        private void PushUndo(Action undoAction, Action redoAction) { if(replaying||undoAction==null||redoAction==null)return; undo.Push(new JournalEntry{Undo=undoAction,Redo=redoAction}); if(undo.Count>128){var keep=undo.Take(128).Reverse().ToArray();undo.Clear();foreach(JournalEntry entry in keep)undo.Push(entry);} }

        private static bool TryUnit(uint id,out Unit unit)=>UnitRegistry.TryGetUnit(new PersistentID{Id=id},out unit);
        private static List<Unit> ResolveUnits(List<uint> ids){var result=new List<Unit>();foreach(uint id in ids)if(TryUnit(id,out Unit unit)&&unit!=null)result.Add(unit);return result;}
        private static void AddIds(HorusCommandResult result,List<Unit> units){foreach(Unit unit in units)if(unit!=null)result.AffectedUnitIds.Add(unit.persistentID.Id);}
        private static GlobalPosition ToGlobal(HorusVector3 p)=>new GlobalPosition(p.X,p.Y,p.Z);
        private static HorusVector3 ToWire(GlobalPosition p)=>new HorusVector3(p.x,p.y,p.z);
        private static int FactionIndex(Unit unit)=>unit?.NetworkHQ?.faction!=null&&FactionRegistry.factions!=null?FactionRegistry.factions.IndexOf(unit.NetworkHQ.faction):-1;
        private static UnitEntry ResolveCatalogEntry(string jsonKey,string source)
        {
            IReadOnlyList<UnitEntry> matches=UnitCatalog.FindAll(jsonKey);
            if(matches.Count==0)return UnitCatalog.Find(jsonKey);
            if(!string.IsNullOrEmpty(source))
            {
                UnitEntry exact=matches.FirstOrDefault(entry=>string.Equals(entry.Source,source,StringComparison.Ordinal));
                if(exact!=null)return exact;
            }
            return matches.Count==1?matches[0]:null;
        }
        private static GlobalPosition ResolveSpawnPosition(UnitEntry entry,GlobalPosition requested)
        {
            if(entry?.Def==null)throw new InvalidOperationException("Spawn definition is unavailable.");
            Vector3 local=requested.ToLocalPosition();
            if(!Finite(local.x)||!Finite(local.y)||!Finite(local.z)||Mathf.Abs(local.x)>100000000f||Mathf.Abs(local.y)>100000000f||Mathf.Abs(local.z)>100000000f)throw new InvalidOperationException("Spawn position is outside supported world bounds.");
            if(entry.SpawnKind==SpawnKind.Ship)
            {
                local.y=Datum.LocalSeaY+entry.Def.spawnOffset.y+(HorusMod.HorusPlugin.ShipSpawnLift?.Value??3f);
                float length=entry.Def.length>0f?entry.Def.length:150f;
                foreach(Unit unit in UnitRegistry.allUnits)
                {
                    if(!(unit is Ship other)||unit.Networkdisabled)continue;
                    float otherLength=other.definition!=null&&other.definition.length>0f?other.definition.length:150f;
                    float minimum=(length+otherLength)*0.5f+25f;
                    Vector3 delta=local-other.transform.position;delta.y=0f;
                    if(delta.sqrMagnitude<minimum*minimum)local=other.transform.position+(delta.sqrMagnitude>0.01f?delta.normalized:other.transform.right)*(minimum+0.5f);
                }
            }
            else if(entry.PlacementSurface==PlacementSurface.Sea)local.y=Datum.LocalSeaY+entry.Def.spawnOffset.y;
            else if(entry.PlacementSurface==PlacementSurface.Ground)
            {
                Vector3 origin=new Vector3(local.x,local.y+30000f,local.z);
                if(!Physics.Raycast(origin,Vector3.down,out RaycastHit hit,60000f,1<<6))throw new InvalidOperationException("No terrain was found at the requested ground position.");
                local.y=hit.point.y+entry.Def.spawnOffset.y+(entry.SpawnKind==SpawnKind.Vehicle?2f:0f);
            }
            return local.ToGlobalPosition();
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private UnitSnapshot CaptureUnit(Unit unit)
        {
            var snapshot=new UnitSnapshot{Definition=unit.definition,Position=unit.GlobalPosition(),Rotation=unit.transform.rotation,HQ=unit.NetworkHQ,Skill=1f,Bravery=0.575f,Fuel=1f,CurrentId=unit.persistentID.Id};
            if(unit is Aircraft aircraft){snapshot.Skill=aircraft.skill;snapshot.Bravery=aircraft.bravery;snapshot.Fuel=aircraft.NetworkfuelLevel;snapshot.Livery=aircraft.NetworkLiveryKey.Index;snapshot.Loadout=HorusLoadoutService.CloneLoadout(aircraft.Networkloadout);}
            else if(unit is GroundVehicle vehicle){snapshot.Skill=vehicle.skill;}
            else if(unit is Ship ship){snapshot.Skill=ship.skill;}
            return snapshot;
        }
        private Unit RestoreSnapshot(UnitSnapshot snapshot)
        {
            if(snapshot?.Definition==null)throw new InvalidOperationException("Journal unit definition is unavailable.");
            var request=new HorusSpawnRequest{Definition=snapshot.Definition,Position=snapshot.Position,Rotation=snapshot.Rotation,HQ=snapshot.HQ,UniqueName=(snapshot.Definition.jsonKey??"horus")+"_journal_"+Guid.NewGuid().ToString("N").Substring(0,8),Skill=snapshot.Skill};
            if(snapshot.Definition is AircraftDefinition||snapshot.Definition.unitPrefab?.GetComponent<Aircraft>()!=null)request.Aircraft=new AircraftSpawnOptions{Loadout=HorusLoadoutService.CloneLoadout(snapshot.Loadout),FuelRatio=snapshot.Fuel,Livery=new LiveryKey(snapshot.Livery),Skill=snapshot.Skill,Bravery=snapshot.Bravery};
            HorusSpawnResult result=HorusSpawnService.Spawn(request);
            if(!result.Success||result.Unit==null)throw new InvalidOperationException("Journal respawn failed: "+result.Message);
            snapshot.CurrentId=result.Unit.persistentID.Id;state.RecordSpawn(snapshot.CurrentId);return result.Unit;
        }
        private void RemoveSnapshot(UnitSnapshot snapshot){if(snapshot!=null&&TryUnit(snapshot.CurrentId,out Unit unit)){DeleteUnit(unit);state.RecordDelete(snapshot.CurrentId);}}
        private static void ApplyDestinations(List<Unit> units,List<GlobalPosition> destinations){for(int i=0;i<units.Count&&i<destinations.Count;i++)if(units[i]!=null)HorusOrders.TrySetDestination(units[i],destinations[i],false,out _);}
        private static void DeleteUnit(Unit unit){if(unit==null)return;if(NetworkManagerNuclearOption.i?.ServerObjectManager!=null&&unit.Identity!=null)NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(unit.Identity);else UnityEngine.Object.Destroy(unit.gameObject);}
        private HorusCommandResult NewResult(HorusCommandEnvelope command)=>new HorusCommandResult{RequestId=command.RequestId,Command=command.Command,Result=HorusResultCode.InternalError,SessionId=state.SessionId,Revision=state.Revision};
        private static void Accept(HorusCommandResult result,string message){result.Result=HorusResultCode.Accepted;result.Message=message;}
        private static void Reject(HorusCommandResult result,HorusResultCode code,string message){result.Result=code;result.Message=message;}
    }
}
