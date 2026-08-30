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
                if(item.Command.ExpectedRevision!=state.Revision)
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
                }
            }
            catch(Exception ex){HorusLog.Warning("Server","Rejected malformed Horus packet: "+ex.Message);}
        }

        private void HandleHello(INetworkPlayer player,HorusHello hello)
        {
            HorusServerClientState client=GetClient(player);
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
            if(!enabled()){SendReject(player,command,HorusResultCode.Disabled,"Horus server is disabled.");return;}
            if(!client.HelloReceived){SendReject(player,command,HorusResultCode.Unauthorized,"Hello handshake is required.");return;}
            ResolveCaller(player,client,out HorusResultCode authCode,out string authReason);
            if(!client.Authorized){SendReject(player,command,authCode,authReason);return;}
            double now=Time.realtimeSinceStartupAsDouble;
            if(!client.MutationRate.TryConsume(now)){SendReject(player,command,HorusResultCode.RateLimited,"Mutation rate limit exceeded.");return;}
            if(!HorusCommandValidator.TryValidate(command,out string error)){SendReject(player,command,HorusResultCode.InvalidPayload,error);return;}
            if(command.SessionId!=state.SessionId){SendReject(player,command,HorusResultCode.InvalidSession,"Mission session changed; resync required.");return;}
            if(command.ExpectedRevision!=state.Revision){SendReject(player,command,HorusResultCode.StaleRevision,"State revision is stale; resync required.");return;}
            if(!client.Deduplicator.TryRemember(command.RequestId,now)){SendReject(player,command,HorusResultCode.DuplicateRequest,"Duplicate requestId.");return;}
            pending.Enqueue(new PendingCommand{Player=player,Command=command,SteamId=client.SteamId});
        }

        private void HandleStateRequest(INetworkPlayer player,HorusStateRequest request)
        {
            HorusServerClientState client=GetClient(player);ResolveCaller(player,client,out _,out _);
            if(!enabled()||!client.Authorized||!client.ReadRate.TryConsume(Time.realtimeSinceStartupAsDouble))return;
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
                units.Add(new HorusUnitState{UnitId=unit.persistentID.Id,DefinitionKey=unit.definition?.jsonKey??"",Name=unit.unitName??"",FactionIndex=faction,Position=new HorusVector3(p.x,p.y,p.z),HorusOwned=state.IsHorusOwned(unit.persistentID.Id)});
            }
            var factories=new List<HorusFactoryState>();
            foreach(RtsFactory f in RtsFactoryManager.Instance.activeFactories)
            {
                var factory=new HorusFactoryState{FactoryId=f.id??"",PresetName=f.presetName??"",FactionIndex=f.factionId,Enabled=f.enabled,ProductionEnabled=f.produceUnits,ConsumesBudget=f.consumeBudgetForProduction,Position=new HorusVector3(f.globalX,f.globalY,f.globalZ),Yaw=f.yaw,GeneratesIncome=f.generateIncome,IncomePerMinute=f.incomePerMinute,CurrentProductionIndex=f.currentProductionIndex,ProductionIntervalSeconds=f.productionIntervalSeconds,ProductionTimer=f.productionTimer,MaxActiveProducedUnits=f.maxActiveProducedUnits,UsesRallyPoint=f.useRallyPoint,RallyPoint=new HorusVector3(f.rallyX,f.rallyY,f.rallyZ),SpawnRadius=f.spawnRadius,LastStatus=f.lastStatus??""};
                if(f.productionUnitKeys!=null)factory.ProductionKeys.AddRange(f.productionUnitKeys.Take(HorusProtocol.MaxMounts));factories.Add(factory);
            }
            Guid snapshot=Guid.NewGuid();int pageSize=8;int pageCount=HorusPaging.ComputePageCount(units.Count,factories.Count,pageSize);
            for(int page=0;page<pageCount;page++)
            {
                var statePage=new HorusStatePage{SessionId=state.SessionId,SnapshotId=snapshot,Revision=state.Revision,PageIndex=page,PageCount=pageCount};
                if(page==0){statePage.RtsMode=(int)RtsEconomyManager.Instance.CurrentMode;statePage.RtsDeployMode=(int)RtsEconomyManager.Instance.DeployMode;}
                statePage.Units.AddRange(units.Skip(page*pageSize).Take(pageSize));statePage.Factories.AddRange(factories.Skip(page*pageSize).Take(pageSize));
                if(page==0&&FactionRegistry.factions!=null)for(int i=0;i<FactionRegistry.factions.Count;i++){FactionEconomyState faction=RtsEconomyManager.Instance.GetFactionState(i);statePage.Budgets.Add(new HorusBudgetState{FactionIndex=i,Budget=RtsEconomyManager.Instance.GetBudget(i),IncomePerTick=faction?.IncomePerTick??0f,UnitCap=faction?.UnitCap??0,ActiveUnitCount=faction?.ActiveUnitCount??0});}
                Send(player,HorusPacketKind.StatePage,statePage);
            }
        }

        private void BroadcastEvent(HorusCommandResult result)
        {
            var evt=new HorusStateEvent{SessionId=state.SessionId,Revision=state.Revision,Result=result};
            foreach(KeyValuePair<INetworkPlayer,HorusServerClientState> pair in clients)
                if(pair.Key!=null&&pair.Key.IsConnected&&pair.Value.Authorized)Send(pair.Key,HorusPacketKind.StateEvent,evt);
        }

        private HorusServerClientState GetClient(INetworkPlayer player)
        {
            if(!clients.TryGetValue(player,out HorusServerClientState stateValue))
            {
                double now=Time.realtimeSinceStartupAsDouble;
                stateValue=new HorusServerClientState{MutationRate=new HorusTokenBucket(10,20,now),ReadRate=new HorusTokenBucket(20,40,now)};clients[player]=stateValue;
            }
            return stateValue;
        }

        private void ResolveCaller(INetworkPlayer player,HorusServerClientState client,out HorusResultCode code,out string reason)
        {
            client.Authorized=false;client.SteamId=0;
            if(player==null||!player.IsAuthenticated){code=HorusResultCode.Unauthorized;reason="Connection is not authenticated.";return;}
            NetworkAuthenticatorNuclearOption.AuthData auth=player.GetAuthData();
            if(auth==null||!auth.UsingSteamTransport){code=HorusResultCode.SteamRequired;reason="Authenticated Steam transport is required.";return;}
            ulong steamId=auth.SteamID.m_SteamID;client.SteamId=steamId;
            if(!allowlist().Contains(steamId)){code=HorusResultCode.Unauthorized;reason="SteamID64 is not allowlisted.";return;}
            client.Authorized=true;code=HorusResultCode.Accepted;reason="Authorized.";
        }

        private void SendReject(INetworkPlayer player,HorusCommandEnvelope command,HorusResultCode code,string message)
        {Send(player,HorusPacketKind.CommandResult,new HorusCommandResult{RequestId=command?.RequestId??Guid.Empty,Command=command?.Command??HorusCommandKind.None,Result=code,SessionId=state.SessionId,Revision=state.Revision,Message=message});}
        private static void Send(INetworkPlayer player,HorusPacketKind kind,object value){if(player==null||!player.IsConnected)return;player.Send(new HorusTransportMessage{Payload=HorusWireCodec.Encode(kind,value)});}
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
