using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HorusMod.Data;
using HorusMod.Economy;
using HorusMod.Logging;
using HorusMod.Shared;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using NuclearOption.Networking.Authentication;
using UnityEngine;

namespace HorusMod.Server
{
    internal sealed class HorusServerTransport
    {
        private sealed class PendingCommand { public INetworkPlayer Player; public HorusCommandEnvelope Command; public ulong SteamId; }
        private readonly HorusServerState state;
        private readonly HorusServerCommandExecutor executor;
        private readonly Func<bool> enabled;
        private readonly Func<HorusAdminAllowlist> allowlist;
        private readonly HorusAuditWriter audit;
        private readonly Queue<PendingCommand> pending = new Queue<PendingCommand>();
        private readonly Dictionary<INetworkPlayer,HorusServerClientState> clients = new Dictionary<INetworkPlayer,HorusServerClientState>();
        private readonly Dictionary<ulong,HorusServerPrincipalState> principals = new Dictionary<ulong,HorusServerPrincipalState>();
        private NetworkServer registeredServer;
        public int ConnectedClientCount=>clients.Count;
        public int AuthorizedClientCount=>clients.Values.Count(client=>client.Authorized);
        public int PendingCommandCount=>pending.Count;

        public HorusServerTransport(HorusServerState state,HorusServerCommandExecutor executor,Func<bool> enabled,Func<HorusAdminAllowlist> allowlist,HorusAuditWriter audit)
        { this.state=state;this.executor=executor;this.enabled=enabled;this.allowlist=allowlist;this.audit=audit;EnsureSerialization(); }

        public void Tick()
        {
            NetworkServer server=NetworkManagerNuclearOption.i?.Server;
            if(server!=registeredServer)
            {
                Unregister();
                if(server!=null&&server.Active)
                {
                    ((IMessageReceiver)server.MessageHandler).RegisterHandler<HorusTransportMessage>(HandleMessage,false);
                    server.Disconnected.AddListener(OnDisconnected);
                    registeredServer=server;
                    HorusLog.Info("Server","Dedicated Mirage handler registered.");
                }
            }
            while(pending.Count>0)
            {
                PendingCommand item=pending.Dequeue();
                HorusCommandResult result;
                HorusServerClientState queuedClient=GetClient(item.Player);
                ResolveCaller(item.Player,queuedClient,out HorusResultCode queuedAuthCode,out string queuedAuthReason);
                if(!enabled())
                    result=NewRejectedResult(item.Command,HorusResultCode.Disabled,"Horus server is disabled.");
                else if(!queuedClient.Authorized)
                    result=NewRejectedResult(item.Command,queuedAuthCode,queuedAuthReason);
                else if(item.Command.ExpectedRevision!=state.Revision)
                    result=new HorusCommandResult{RequestId=item.Command.RequestId,Command=item.Command.Command,Result=HorusResultCode.StaleRevision,SessionId=state.SessionId,Revision=state.Revision,Message="State revision changed while the command was queued; resync required."};
                else result=executor.Execute(item.Command);
                Send(item.Player,HorusPacketKind.CommandResult,result);
                audit.Write(item.SteamId,CurrentMissionName(),item.Command,result);
                if(result.Result==HorusResultCode.Accepted)BroadcastEvent(result);
            }
        }

        public void ResetMission()
        {
            state.BeginMission();executor.ResetMission();pending.Clear();
            foreach(KeyValuePair<INetworkPlayer,HorusServerClientState> pair in clients.ToArray())
            {
                ResolveCaller(pair.Key,pair.Value,out HorusResultCode code,out string reason);pair.Value.HelloReceived=true;
                Send(pair.Key,HorusPacketKind.Capabilities,new HorusCapabilities{ServerVersion=HorusServerPlugin.PluginVersion,SessionId=state.SessionId,Revision=state.Revision,Features=HorusCapability.FullParity,Authorized=pair.Value.Authorized&&enabled(),Result=!enabled()?HorusResultCode.Disabled:code,Message=!enabled()?"Horus server is disabled.":reason});
            }
        }

        public void RefreshAuthorization(bool force=false)
        {
            foreach(KeyValuePair<INetworkPlayer,HorusServerClientState> pair in clients.ToArray())
            {
                bool wasAuthorized=pair.Value.Authorized;
                ResolveCaller(pair.Key,pair.Value,out HorusResultCode code,out string reason);
                if(!pair.Value.HelloReceived||(!force&&wasAuthorized==pair.Value.Authorized))continue;
                Send(pair.Key,HorusPacketKind.Capabilities,new HorusCapabilities
                {
                    ServerVersion=HorusServerPlugin.PluginVersion,SessionId=state.SessionId,Revision=state.Revision,
                    Features=HorusCapability.FullParity,Authorized=pair.Value.Authorized&&enabled(),
                    Result=!enabled()?HorusResultCode.Disabled:code,Message=!enabled()?"Horus server is disabled.":reason
                });
            }
        }
        public void Unregister()
        {
            if(registeredServer!=null)
            {
                try{((IMessageReceiver)registeredServer.MessageHandler).UnregisterHandler<HorusTransportMessage>();registeredServer.Disconnected.RemoveListener(OnDisconnected);}catch{}
            }
            registeredServer=null;clients.Clear();pending.Clear();
        }

        private void HandleMessage(INetworkPlayer player,HorusTransportMessage message)
        {
            try
            {
                object decoded=HorusWireCodec.Decode(message?.Payload,out HorusPacketKind kind);
                switch(kind)
                {
                    case HorusPacketKind.Hello:HandleHello(player,(HorusHello)decoded);break;
                    case HorusPacketKind.Command:HandleCommand(player,(HorusCommandEnvelope)decoded);break;
                    case HorusPacketKind.StateRequest:HandleStateRequest(player,(HorusStateRequest)decoded);break;
                    default:if(GetClient(player).ConnectionRate.TryConsume(Time.realtimeSinceStartupAsDouble))HorusLog.Warning("Server","Rejected client-origin Horus packet kind: "+kind);break;
                }
            }
            catch(Exception ex){if(GetClient(player).ConnectionRate.TryConsume(Time.realtimeSinceStartupAsDouble))HorusLog.Warning("Server","Rejected malformed Horus packet: "+ex.Message);}
        }

        private void HandleHello(INetworkPlayer player,HorusHello hello)
        {
            HorusServerClientState client=GetClient(player);
            if(!client.ConnectionRate.TryConsume(Time.realtimeSinceStartupAsDouble))return;
            ResolveCaller(player,client,out HorusResultCode code,out string reason);
            if(hello.ProtocolVersion!=HorusProtocol.Version){code=HorusResultCode.ProtocolMismatch;reason="Protocol mismatch.";client.Authorized=false;}
            client.HelloReceived=true;
            Send(player,HorusPacketKind.Capabilities,new HorusCapabilities
            {
                ServerVersion=HorusServerPlugin.PluginVersion,SessionId=state.SessionId,Revision=state.Revision,
                Features=HorusCapability.FullParity,Authorized=client.Authorized&&enabled(),
                Result=!enabled()?HorusResultCode.Disabled:code,Message=!enabled()?"Horus server is disabled.":reason
            });
        }

        private void HandleCommand(INetworkPlayer player,HorusCommandEnvelope command)
        {
            HorusServerClientState client=GetClient(player);
            double now=Time.realtimeSinceStartupAsDouble;
            if(!client.ConnectionRate.TryConsume(now))return;
            if(!enabled()){SendReject(player,command,HorusResultCode.Disabled,"Horus server is disabled.");return;}
            if(!client.HelloReceived){SendReject(player,command,HorusResultCode.Unauthorized,"Hello handshake is required.");return;}
            ResolveCaller(player,client,out HorusResultCode authCode,out string authReason);
            if(!client.Authorized){SendReject(player,command,authCode,authReason);return;}
            HorusServerPrincipalState principal=GetPrincipal(client.SteamId,now);
            if(!principal.MutationRate.TryConsume(now)){SendReject(player,command,HorusResultCode.RateLimited,"Mutation rate limit exceeded.");return;}
            if(!HorusCommandValidator.TryValidate(command,out string error)){SendReject(player,command,HorusResultCode.InvalidPayload,error);return;}
            if(command.SessionId!=state.SessionId){SendReject(player,command,HorusResultCode.InvalidSession,"Mission session changed; resync required.");return;}
            if(command.ExpectedRevision!=state.Revision){SendReject(player,command,HorusResultCode.StaleRevision,"State revision is stale; resync required.");return;}
            if(!principal.Deduplicator.TryRemember(command.RequestId,now)){SendReject(player,command,HorusResultCode.DuplicateRequest,"Duplicate requestId.");return;}
            pending.Enqueue(new PendingCommand{Player=player,Command=command,SteamId=client.SteamId});
        }

        private void HandleStateRequest(INetworkPlayer player,HorusStateRequest request)
        {
            HorusServerClientState client=GetClient(player);double now=Time.realtimeSinceStartupAsDouble;
            if(!client.ConnectionRate.TryConsume(now))return;
            ResolveCaller(player,client,out HorusResultCode code,out string reason);
            if(!enabled()||!client.Authorized){SendCurrentCapabilities(player,client,!enabled()?HorusResultCode.Disabled:code,!enabled()?"Horus server is disabled.":reason);return;}
            if(!GetPrincipal(client.SteamId,now).ReadRate.TryConsume(now))return;
            if(request==null||request.SessionId!=state.SessionId)
            {
                SendCurrentCapabilities(player,client,HorusResultCode.Accepted,"Mission session refreshed.");return;
            }
            SendSnapshot(player);
        }

        private void SendSnapshot(INetworkPlayer player)
        {
            var units=new List<HorusUnitState>();
            foreach(Unit unit in UnitRegistry.allUnits)
            {
                if(unit==null||unit.Networkdisabled)continue;
                int faction=unit.NetworkHQ?.faction!=null&&FactionRegistry.factions!=null?FactionRegistry.factions.IndexOf(unit.NetworkHQ.faction):-1;
                GlobalPosition p=unit.GlobalPosition();
                string definitionKey=HorusWireText.Clamp(unit.definition?.jsonKey);var position=new HorusVector3(p.x,p.y,p.z);
                if(unit.persistentID.Id==0||string.IsNullOrWhiteSpace(definitionKey)||!HorusWireText.IsStableKey(definitionKey)||!HorusPersistencePolicy.IsSafePosition(p.x,p.y,p.z))continue;
                units.Add(new HorusUnitState{UnitId=unit.persistentID.Id,DefinitionKey=definitionKey,Name=HorusWireText.SanitizeVisible(unit.unitName),FactionIndex=faction,Position=position,HorusOwned=state.IsHorusOwned(unit.persistentID.Id)});
            }
            var factories=new List<HorusFactoryState>();
            foreach(RtsFactory f in RtsFactoryManager.Instance.activeFactories)
            {
                if(f==null||!HorusPersistencePolicy.IsSafeStringCollection(f.productionUnitKeys??new List<string>(),HorusProtocol.MaxMounts,out _)){HorusLog.Warning("Server","Skipped invalid factory state during snapshot.");continue;}
                var factory=new HorusFactoryState{FactoryId=HorusWireText.Clamp(f.id),PresetName=HorusWireText.Clamp(f.presetName),FactionIndex=f.factionId,Enabled=f.enabled,ProductionEnabled=f.produceUnits,ConsumesBudget=f.consumeBudgetForProduction,Position=new HorusVector3(f.globalX,f.globalY,f.globalZ),Yaw=f.yaw,GeneratesIncome=f.generateIncome,IncomePerMinute=f.incomePerMinute,CurrentProductionIndex=f.currentProductionIndex,ProductionIntervalSeconds=f.productionIntervalSeconds,ProductionTimer=f.productionTimer,MaxActiveProducedUnits=f.maxActiveProducedUnits,UsesRallyPoint=f.useRallyPoint,RallyPoint=new HorusVector3(f.rallyX,f.rallyY,f.rallyZ),SpawnRadius=f.spawnRadius,LastStatus=HorusWireText.SanitizeVisible(f.lastStatus)};
                factory.ProductionKeys.AddRange(f.productionUnitKeys);
                var candidate=new HorusStatePage{SessionId=state.SessionId,SnapshotId=Guid.NewGuid(),PageIndex=0,PageCount=1};candidate.Factories.Add(factory);
                if(!HorusSnapshotPolicy.IsValidPageShape(candidate)){HorusLog.Warning("Server","Skipped invalid factory state during snapshot.");continue;}factories.Add(factory);
            }
            var pages=new List<HorusStatePage>();Guid snapshot=Guid.NewGuid();
            var headerPage=new HorusStatePage{SessionId=state.SessionId,SnapshotId=snapshot,Revision=state.Revision,RtsMode=(int)RtsEconomyManager.Instance.CurrentMode,RtsDeployMode=(int)RtsEconomyManager.Instance.DeployMode};
            if(FactionRegistry.factions!=null)for(int i=0;i<FactionRegistry.factions.Count&&headerPage.Budgets.Count<HorusProtocol.MaxSnapshotBudgetsPerPage;i++){FactionEconomyState faction=RtsEconomyManager.Instance.GetFactionState(i);var budget=new HorusBudgetState{FactionIndex=i,Budget=RtsEconomyManager.Instance.GetBudget(i),IncomePerTick=faction?.IncomePerTick??0f,UnitCap=faction?.UnitCap??0,ActiveUnitCount=faction?.ActiveUnitCount??0};if(HorusPersistencePolicy.IsFinite(budget.Budget)&&HorusPersistencePolicy.IsFinite(budget.IncomePerTick)&&budget.Budget>=0f&&budget.Budget<=1000000000f&&Math.Abs(budget.IncomePerTick)<=1000000000f&&budget.UnitCap>=0&&budget.UnitCap<=100000&&budget.ActiveUnitCount>=0)headerPage.Budgets.Add(budget);}
            pages.Add(headerPage);
            for(int offset=0;offset<units.Count;offset+=HorusProtocol.MaxSnapshotUnitsPerPage)
            {
                var page=new HorusStatePage{SessionId=state.SessionId,SnapshotId=snapshot,Revision=state.Revision};page.Units.AddRange(units.Skip(offset).Take(HorusProtocol.MaxSnapshotUnitsPerPage));pages.Add(page);
            }
            foreach(HorusFactoryState factory in factories){var page=new HorusStatePage{SessionId=state.SessionId,SnapshotId=snapshot,Revision=state.Revision};page.Factories.Add(factory);pages.Add(page);}
            if(pages.Count>HorusProtocol.MaxSnapshotPages){HorusLog.Warning("Server","Snapshot exceeds the supported page limit.");return;}
            for(int i=0;i<pages.Count;i++){pages[i].PageIndex=i;pages[i].PageCount=pages.Count;}
            if(!HorusSnapshotPolicy.IsCoherentSnapshot(pages)){HorusLog.Warning("Server","Snapshot coherence validation failed closed before transmission.");return;}
            foreach(HorusStatePage page in pages)Send(player,HorusPacketKind.StatePage,page);
        }

        private void BroadcastEvent(HorusCommandResult result)
        {
            var evt=new HorusStateEvent{SessionId=state.SessionId,Revision=state.Revision,Result=result};
            foreach(KeyValuePair<INetworkPlayer,HorusServerClientState> pair in clients)
            {
                if(pair.Key==null||!pair.Key.IsConnected)continue;bool wasAuthorized=pair.Value.Authorized;ResolveCaller(pair.Key,pair.Value,out HorusResultCode code,out string reason);
                if(pair.Value.Authorized)Send(pair.Key,HorusPacketKind.StateEvent,evt);else if(wasAuthorized)SendCurrentCapabilities(pair.Key,pair.Value,code,reason);
            }
        }

        private HorusServerClientState GetClient(INetworkPlayer player)
        {
            if(!clients.TryGetValue(player,out HorusServerClientState stateValue))
            {
                double now=Time.realtimeSinceStartupAsDouble;
                stateValue=new HorusServerClientState{ConnectionRate=new HorusTokenBucket(20,40,now)};clients[player]=stateValue;
            }
            return stateValue;
        }

        private HorusServerPrincipalState GetPrincipal(ulong steamId,double now)
        {
            if(!principals.TryGetValue(steamId,out HorusServerPrincipalState principal))
            {
                principal=new HorusServerPrincipalState{MutationRate=new HorusTokenBucket(10,20,now),ReadRate=new HorusTokenBucket(2,4,now)};
                principals[steamId]=principal;
            }
            return principal;
        }

        private void ResolveCaller(INetworkPlayer player,HorusServerClientState client,out HorusResultCode code,out string reason)
        {
            client.Authorized=false;client.SteamId=0;
            if(player==null||!player.IsAuthenticated){code=HorusResultCode.Unauthorized;reason="Connection is not authenticated.";return;}
            NetworkAuthenticatorNuclearOption.AuthData auth=player.GetAuthData();
            if(auth==null||!auth.UsingSteamTransport||!auth.SteamSessionOk){code=HorusResultCode.SteamRequired;reason="An active authenticated Steam session is required.";return;}
            ulong steamId=auth.SteamID.m_SteamID;client.SteamId=steamId;
            if(!allowlist().Contains(steamId)){code=HorusResultCode.Unauthorized;reason="SteamID64 is not allowlisted.";return;}
            client.Authorized=true;code=HorusResultCode.Accepted;reason="Authorized.";
        }

        private void SendReject(INetworkPlayer player,HorusCommandEnvelope command,HorusResultCode code,string message)
        {
            HorusCommandResult result=NewRejectedResult(command,code,message);Send(player,HorusPacketKind.CommandResult,result);
            if(command!=null)audit.Write(TryAuthenticatedSteamId(player),CurrentMissionName(),command,result);
        }
        private HorusCommandResult NewRejectedResult(HorusCommandEnvelope command,HorusResultCode code,string message)=>new HorusCommandResult{RequestId=command?.RequestId??Guid.Empty,Command=command?.Command??HorusCommandKind.None,Result=code,SessionId=state.SessionId,Revision=state.Revision,Message=HorusWireText.SanitizeVisible(message)};
        private void SendCurrentCapabilities(INetworkPlayer player,HorusServerClientState client,HorusResultCode code,string message)=>Send(player,HorusPacketKind.Capabilities,new HorusCapabilities{ServerVersion=HorusServerPlugin.PluginVersion,SessionId=state.SessionId,Revision=state.Revision,Features=HorusCapability.FullParity,Authorized=client!=null&&client.Authorized&&enabled(),Result=!enabled()?HorusResultCode.Disabled:code,Message=!enabled()?"Horus server is disabled.":HorusWireText.SanitizeVisible(message)});
        private static ulong TryAuthenticatedSteamId(INetworkPlayer player)
        {
            if(player==null||!player.IsAuthenticated)return 0;NetworkAuthenticatorNuclearOption.AuthData auth=player.GetAuthData();return auth!=null&&auth.UsingSteamTransport?auth.SteamID.m_SteamID:0;
        }
        private static void Send(INetworkPlayer player,HorusPacketKind kind,object value)
        {
            if(player==null||!player.IsConnected)return;
            try{player.Send(new HorusTransportMessage{Payload=HorusWireCodec.Encode(kind,value)});}
            catch(Exception ex){HorusLog.Warning("Server","Failed to encode Horus response: "+ex.Message);}
        }
        private void OnDisconnected(INetworkPlayer player){clients.Remove(player);}
        private static string CurrentMissionName()=>MissionManager.IsRunning&&MissionManager.CurrentMission!=null?MissionManager.CurrentMission.Name:"none";

        internal static void EnsureSerialization()
        {
            Writer<HorusTransportMessage>.Write=(writer,value)=>writer.WriteBytesAndSize(value?.Payload??Array.Empty<byte>(),HorusProtocol.MaxMessageBytes);
            Reader<HorusTransportMessage>.Read=reader=>new HorusTransportMessage{Payload=reader.ReadBytesAndSize(HorusProtocol.MaxMessageBytes)};
            try{MessagePacker.RegisterMessage<HorusTransportMessage>();}catch(ArgumentException){}
        }
    }
}
