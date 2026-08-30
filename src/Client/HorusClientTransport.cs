using System;
using System.Collections.Generic;
using System.Linq;
using HorusMod.Economy;
using HorusMod.Logging;
using HorusMod.Shared;
using HorusMod.UI;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace HorusMod.Client
{
    public sealed class HorusClientTransport : MonoBehaviour, IHorusCommandGateway
    {
        private readonly Dictionary<Guid,HorusCommandKind> pending = new Dictionary<Guid,HorusCommandKind>();
        private readonly Queue<HorusCommandEnvelope> outbound = new Queue<HorusCommandEnvelope>();
        private readonly Dictionary<int,HorusStatePage> pages = new Dictionary<int,HorusStatePage>();
        private Guid inFlightRequestId;
        private NetworkClient registeredClient;
        private bool helloSent;
        private Guid snapshotId;
        private int snapshotPageCount;
        private float nextCommandSendTime;

        public static HorusClientTransport Instance { get; private set; }
        public bool IsReady => IsRemoteClient && Authorized && SessionId != Guid.Empty && registeredClient != null && registeredClient.IsConnected;
        public bool Authorized { get; private set; }
        public Guid SessionId { get; private set; }
        public ulong Revision { get; private set; }
        public string ServerVersion { get; private set; } = "";
        public string Status { get; private set; } = "Not connected";
        public HorusCapability Capabilities { get; private set; }
        public static bool ApplyingSnapshot { get; private set; }

        public static bool IsRemoteClient
        {
            get
            {
                if(GameManager.gameState!=GameState.Multiplayer)return false;
                NetworkManagerNuclearOption manager=NetworkManagerNuclearOption.i;
                return manager!=null&&manager.Client!=null&&manager.Client.Active&&(manager.Server==null||!manager.Server.Active);
            }
        }

        private void Awake(){Instance=this;DontDestroyOnLoad(gameObject);EnsureSerialization();}
        private void OnDestroy(){Unregister();if(Instance==this)Instance=null;}

        private void Update()
        {
            NetworkClient client=IsRemoteClient?NetworkManagerNuclearOption.i?.Client:null;
            if(client!=registeredClient)
            {
                Unregister();
                if(client!=null&&client.Active)
                {
                    ((IMessageReceiver)client.MessageHandler).RegisterHandler<HorusTransportMessage>(HandleMessage,false);
                    registeredClient=client;helloSent=false;Authorized=false;SessionId=Guid.Empty;Revision=0;Status="Waiting for authentication";
                }
            }
            if(registeredClient!=null&&registeredClient.IsConnected&&registeredClient.Player!=null&&registeredClient.Player.IsAuthenticated&&!helloSent)
            {
                helloSent=true;Send(HorusPacketKind.Hello,new HorusHello{ClientVersion=HorusPlugin.PluginVersion});Status="Handshake sent";
            }
            TrySendNext();
        }

        public bool TrySubmit(HorusCommandKind command,HorusCommandPayload payload,out Guid requestId)
        {
            requestId=Guid.NewGuid();
            if(!IsReady){Status=Authorized?"Server state is not ready":"Horus permission was not granted";return false;}
            var envelope=new HorusCommandEnvelope{SessionId=SessionId,RequestId=requestId,Command=command,Payload=payload??new HorusCommandPayload()};
            if(!HorusCommandValidator.TryValidate(envelope,out string error)){Status=error;return false;}
            pending[requestId]=command;outbound.Enqueue(envelope);TrySendNext();return true;
        }

        public void RequestSnapshot(){if(registeredClient==null||SessionId==Guid.Empty)return;Send(HorusPacketKind.StateRequest,new HorusStateRequest{SessionId=SessionId,KnownRevision=Revision});}

        private void HandleMessage(HorusTransportMessage message)
        {
            try
            {
                object decoded=HorusWireCodec.Decode(message?.Payload,out HorusPacketKind kind);
                switch(kind)
                {
                    case HorusPacketKind.Capabilities:HandleCapabilities((HorusCapabilities)decoded);break;
                    case HorusPacketKind.CommandResult:HandleResult((HorusCommandResult)decoded);break;
                    case HorusPacketKind.StateEvent:HandleEvent((HorusStateEvent)decoded);break;
                    case HorusPacketKind.StatePage:HandlePage((HorusStatePage)decoded);break;
                }
            }
            catch(Exception ex){Status="Malformed Horus server response";HorusLog.Warning("ClientNet",ex.Message);}
        }

        private void HandleCapabilities(HorusCapabilities value)
        {
            ServerVersion=value.ServerVersion;SessionId=value.SessionId;Revision=value.Revision;Capabilities=value.Features;Authorized=value.Authorized;Status=value.Message;
            inFlightRequestId=Guid.Empty;pending.Clear();outbound.Clear();
            HorusToasts.Show(value.Authorized?"Horus dedicated authority granted":"Horus dedicated: "+value.Message);
            if(value.Authorized)RequestSnapshot();
        }
        private void HandleResult(HorusCommandResult value)
        {
            pending.Remove(value.RequestId);if(inFlightRequestId==value.RequestId)inFlightRequestId=Guid.Empty;SessionId=value.SessionId;Revision=value.Revision;Status=value.Message;
            HorusToasts.Show(value.Result==HorusResultCode.Accepted?value.Message:"Horus rejected: "+value.Message);
            if(value.Result==HorusResultCode.Accepted)RequestSnapshot();
            if(value.Result==HorusResultCode.InvalidSession||value.Result==HorusResultCode.StaleRevision){outbound.Clear();RequestSnapshot();}
            TrySendNext();
        }
        private void HandleEvent(HorusStateEvent value)
        {
            if(value.SessionId!=SessionId){SessionId=value.SessionId;Revision=0;RequestSnapshot();return;}
            if(value.Revision!=Revision+1&&value.Revision!=Revision){RequestSnapshot();return;}
            Revision=value.Revision;
        }
        private void HandlePage(HorusStatePage page)
        {
            if(page.SnapshotId!=snapshotId){snapshotId=page.SnapshotId;snapshotPageCount=page.PageCount;pages.Clear();}
            if(page.PageIndex<0||page.PageIndex>=snapshotPageCount)return;pages[page.PageIndex]=page;
            if(pages.Count!=snapshotPageCount)return;
            var all=pages.OrderBy(pair=>pair.Key).Select(pair=>pair.Value).ToList();SessionId=page.SessionId;Revision=page.Revision;
            ApplySnapshot(all);Status="Dedicated state synchronized";
        }

        private static void ApplySnapshot(List<HorusStatePage> statePages)
        {
            ApplyingSnapshot=true;
            try
            {
                RtsEconomyManager economy=RtsEconomyManager.Instance;
                RtsFactoryManager factories=RtsFactoryManager.Instance;
                if(factories!=null)
                {
                    factories.activeFactories.Clear();
                    foreach(HorusFactoryState value in statePages.SelectMany(page=>page.Factories))
                        factories.activeFactories.Add(new RtsFactory{id=value.FactoryId,presetName=value.PresetName,displayName=value.PresetName,factionId=value.FactionIndex,enabled=value.Enabled,produceUnits=value.ProductionEnabled,consumeBudgetForProduction=value.ConsumesBudget,globalX=value.Position.X,globalY=value.Position.Y,globalZ=value.Position.Z,yaw=value.Yaw,generateIncome=value.GeneratesIncome,incomePerMinute=value.IncomePerMinute,productionUnitKeys=new List<string>(value.ProductionKeys),currentProductionIndex=value.CurrentProductionIndex,productionIntervalSeconds=value.ProductionIntervalSeconds,productionTimer=value.ProductionTimer,maxActiveProducedUnits=value.MaxActiveProducedUnits,useRallyPoint=value.UsesRallyPoint,rallyX=value.RallyPoint.X,rallyY=value.RallyPoint.Y,rallyZ=value.RallyPoint.Z,spawnRadius=value.SpawnRadius,lastStatus=value.LastStatus,isVirtual=true});
                }
                if(economy!=null)foreach(HorusBudgetState value in statePages.SelectMany(page=>page.Budgets)){economy.SetBudget(value.FactionIndex,value.Budget);FactionEconomyState faction=economy.GetFactionState(value.FactionIndex);if(faction!=null){faction.IncomePerTick=value.IncomePerTick;faction.UnitCap=value.UnitCap;faction.ActiveUnitCount=value.ActiveUnitCount;}}
                HorusStatePage first=statePages.FirstOrDefault(page=>page.RtsMode>=0);
                if(economy!=null&&first!=null){economy.CurrentMode=(HorusMode)first.RtsMode;economy.DeployMode=(RtsDeployMode)first.RtsDeployMode;}
            }
            finally{ApplyingSnapshot=false;}
        }

        private void TrySendNext()
        {
            if(inFlightRequestId!=Guid.Empty||outbound.Count==0||!IsReady||Time.unscaledTime<nextCommandSendTime)return;
            HorusCommandEnvelope envelope=outbound.Dequeue();
            envelope.SessionId=SessionId;envelope.ExpectedRevision=Revision;
            inFlightRequestId=envelope.RequestId;
            nextCommandSendTime=Time.unscaledTime+0.11f;
            Send(HorusPacketKind.Command,envelope);Status=envelope.Command+" request sent";
        }

        private void Send(HorusPacketKind kind,object value){registeredClient?.Send(new HorusTransportMessage{Payload=HorusWireCodec.Encode(kind,value)});}
        private void Unregister(){if(registeredClient!=null)try{((IMessageReceiver)registeredClient.MessageHandler).UnregisterHandler<HorusTransportMessage>();}catch{}registeredClient=null;helloSent=false;Authorized=false;inFlightRequestId=Guid.Empty;pending.Clear();outbound.Clear();pages.Clear();}
        private static void EnsureSerialization(){Writer<HorusTransportMessage>.Write=(writer,value)=>writer.WriteBytesAndSize(value?.Payload??Array.Empty<byte>(),HorusProtocol.MaxMessageBytes);Reader<HorusTransportMessage>.Read=reader=>new HorusTransportMessage{Payload=reader.ReadBytesAndSize(HorusProtocol.MaxMessageBytes)};try{MessagePacker.RegisterMessage<HorusTransportMessage>();}catch(ArgumentException){}}
    }

    public static class HorusRemoteAuthority
    {
        public static bool IsRemoteSession=>HorusClientTransport.IsRemoteClient;
        public static bool IsAuthorized=>HorusClientTransport.Instance?.IsReady==true;
        public static string Status=>HorusClientTransport.Instance?.Status??"Not connected";
        public static bool TrySubmit(HorusCommandKind kind,HorusCommandPayload payload)=>HorusClientTransport.Instance!=null&&HorusClientTransport.Instance.TrySubmit(kind,payload,out _);
        public static uint UnitId(Unit unit)=>unit!=null?unit.persistentID.Id:0;
        public static HorusVector3 Point(GlobalPosition value)=>new HorusVector3(value.x,value.y,value.z);
        public static HorusVector3 Point(Vector3 value)=>new HorusVector3(value.x,value.y,value.z);
        public static HorusCommandPayload Units(IEnumerable<Unit> units)
        {
            var payload=new HorusCommandPayload();if(units!=null)foreach(Unit unit in units)if(unit!=null&&payload.UnitIds.Count<HorusProtocol.MaxEntitiesPerCommand)payload.UnitIds.Add(unit.persistentID.Id);return payload;
        }
    }
}
