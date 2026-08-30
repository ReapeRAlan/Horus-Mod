using System;
using System.Collections.Generic;

namespace HorusMod.Shared
{
    public static class HorusProtocol
    {
        public const ushort Version = 2;
        public const int MaxMessageBytes = 16 * 1024;
        public const int MaxEntitiesPerCommand = 64;
        public const int MaxWaypointsPerCommand = 32;
        public const int MaxStringBytes = 512;
        public const int MaxMounts = 64;
    }

    public enum HorusPacketKind : byte
    {
        Hello = 1,
        Capabilities = 2,
        Command = 3,
        CommandResult = 4,
        StateRequest = 5,
        StatePage = 6,
        StateEvent = 7
    }

    public enum HorusCommandKind : ushort
    {
        None = 0,
        Spawn = 1,
        Delete = 2,
        Duplicate = 3,
        Move = 4,
        Hold = 5,
        ClearOrders = 6,
        AttackTarget = 7,
        AttackMove = 8,
        Patrol = 9,
        Guard = 10,
        SetRulesOfEngagement = 11,
        SetLoadout = 12,
        SetLivery = 13,
        SetSkill = 14,
        SetFuel = 15,
        SetBudget = 16,
        AdjustBudget = 17,
        CreateFactory = 18,
        DeleteFactory = 19,
        SetFactoryEnabled = 20,
        SetFactoryProductionEnabled = 21,
        SetFactoryConsumesBudget = 22,
        QueueFactoryUnit = 23,
        RemoveFactoryQueueItem = 24,
        ClearFactoryQueue = 25,
        SetFactoryRally = 26,
        ClearFactoryRally = 27,
        StartAllFactories = 28,
        StopAllFactories = 29,
        ReloadFactories = 30,
        ResetFactoryPresets = 31,
        SaveFactories = 32,
        LoadFactories = 33,
        Undo = 34,
        Redo = 35,
        SetRtsMode = 36,
        SetRtsDeployMode = 37,
        AdjustUnitCap = 38
    }

    public enum HorusResultCode : ushort
    {
        Accepted = 0,
        Disabled = 1,
        Unauthorized = 2,
        SteamRequired = 3,
        ProtocolMismatch = 4,
        InvalidSession = 5,
        StaleRevision = 6,
        DuplicateRequest = 7,
        RateLimited = 8,
        InvalidPayload = 9,
        NotFound = 10,
        PolicyDenied = 11,
        NativeFailure = 12,
        Unsupported = 13,
        InternalError = 14
    }

    [Flags]
    public enum HorusCapability : ulong
    {
        None = 0,
        Spawn = 1UL << 0,
        Delete = 1UL << 1,
        Orders = 1UL << 2,
        TacticalOrders = 1UL << 3,
        Loadouts = 1UL << 4,
        UnitEditing = 1UL << 5,
        Economy = 1UL << 6,
        Factories = 1UL << 7,
        LiveOrdnance = 1UL << 8,
        UndoRedo = 1UL << 9,
        StateSync = 1UL << 10,
        Audit = 1UL << 11,
        FullParity = Spawn | Delete | Orders | TacticalOrders | Loadouts | UnitEditing |
                     Economy | Factories | LiveOrdnance | UndoRedo | StateSync | Audit
    }

    public struct HorusVector3
    {
        public float X;
        public float Y;
        public float Z;

        public HorusVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool IsFinite => IsFiniteNumber(X) && IsFiniteNumber(Y) && IsFiniteNumber(Z);

        private static bool IsFiniteNumber(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class HorusTransportMessage
    {
        public byte[] Payload;
    }

    public sealed class HorusHello
    {
        public ushort ProtocolVersion = HorusProtocol.Version;
        public string ClientVersion = "";
    }

    public sealed class HorusCapabilities
    {
        public ushort ProtocolVersion = HorusProtocol.Version;
        public string ServerVersion = "";
        public Guid SessionId;
        public ulong Revision;
        public HorusCapability Features;
        public bool Authorized;
        public HorusResultCode Result;
        public string Message = "";
    }

    public sealed class HorusCallerContext
    {
        public ulong SteamId;
        public bool UsingSteamTransport;
        public bool IsAuthenticated;
        public bool IsHost;
    }

    public interface IHorusCommandGateway
    {
        bool IsReady { get; }
        bool TrySubmit(HorusCommandKind command, HorusCommandPayload payload, out Guid requestId);
    }

    public sealed class HorusCommandPayload
    {
        public readonly List<uint> UnitIds = new List<uint>();
        public readonly List<HorusVector3> Points = new List<HorusVector3>();
        public readonly List<string> MountKeys = new List<string>();
        public uint TargetUnitId;
        public string DefinitionKey = "";
        public string SecondaryKey = "";
        public string FactoryId = "";
        public string PresetName = "";
        public string UniqueName = "";
        public int FactionIndex = -1;
        public int IntValue;
        public float FloatValue;
        public float FloatValue2;
        public float FloatValue3;
        public float Yaw;
        public bool BoolValue;
    }

    public sealed class HorusCommandEnvelope
    {
        public ushort ProtocolVersion = HorusProtocol.Version;
        public Guid SessionId;
        public ulong ExpectedRevision;
        public Guid RequestId;
        public HorusCommandKind Command;
        public HorusCommandPayload Payload = new HorusCommandPayload();
    }

    public sealed class HorusCommandResult
    {
        public Guid RequestId;
        public HorusCommandKind Command;
        public HorusResultCode Result;
        public Guid SessionId;
        public ulong Revision;
        public string Message = "";
        public readonly List<uint> AffectedUnitIds = new List<uint>();
    }

    public sealed class HorusStateRequest
    {
        public Guid SessionId;
        public ulong KnownRevision;
    }

    public sealed class HorusUnitState
    {
        public uint UnitId;
        public string DefinitionKey = "";
        public string Name = "";
        public int FactionIndex = -1;
        public HorusVector3 Position;
        public bool HorusOwned;
    }

    public sealed class HorusFactoryState
    {
        public string FactoryId = "";
        public string PresetName = "";
        public int FactionIndex = -1;
        public bool Enabled;
        public bool ProductionEnabled;
        public bool ConsumesBudget;
        public HorusVector3 Position;
        public float Yaw;
        public bool GeneratesIncome;
        public float IncomePerMinute;
        public readonly List<string> ProductionKeys = new List<string>();
        public int CurrentProductionIndex;
        public float ProductionIntervalSeconds;
        public float ProductionTimer;
        public int MaxActiveProducedUnits;
        public bool UsesRallyPoint;
        public HorusVector3 RallyPoint;
        public float SpawnRadius;
        public string LastStatus = "";
    }

    public sealed class HorusBudgetState
    {
        public int FactionIndex;
        public float Budget;
        public float IncomePerTick;
        public int UnitCap;
        public int ActiveUnitCount;
    }

    public sealed class HorusStatePage
    {
        public Guid SessionId;
        public Guid SnapshotId;
        public ulong Revision;
        public int PageIndex;
        public int PageCount;
        public int RtsMode = -1;
        public int RtsDeployMode = -1;
        public readonly List<HorusUnitState> Units = new List<HorusUnitState>();
        public readonly List<HorusFactoryState> Factories = new List<HorusFactoryState>();
        public readonly List<HorusBudgetState> Budgets = new List<HorusBudgetState>();
    }

    public sealed class HorusStateEvent
    {
        public Guid SessionId;
        public ulong Revision;
        public HorusCommandResult Result = new HorusCommandResult();
    }
}
