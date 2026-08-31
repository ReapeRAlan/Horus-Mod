using System;
using System.Collections.Generic;
using HorusMod.Shared;

namespace HorusMod.Server
{
    internal sealed class HorusServerState
    {
        private readonly HashSet<uint> horusOwned = new HashSet<uint>();
        public Guid SessionId { get; private set; } = Guid.NewGuid();
        public ulong Revision { get; private set; }

        public void BeginMission()
        {
            SessionId = Guid.NewGuid();
            Revision = 0;
            horusOwned.Clear();
        }

        public ulong AdvanceRevision() => ++Revision;
        public void RecordSpawn(uint unitId) { if (unitId != 0) horusOwned.Add(unitId); }
        public void RecordDelete(uint unitId) { if (unitId != 0) horusOwned.Remove(unitId); }
        public bool IsHorusOwned(uint unitId) => unitId != 0 && horusOwned.Contains(unitId);
        public IReadOnlyCollection<uint> HorusOwnedIds => horusOwned;
    }

#if !HORUS_LOGIC_TESTS
    internal sealed class HorusServerClientState
    {
        public HorusTokenBucket ConnectionRate;
        public bool HelloReceived;
        public ushort HelloProtocolVersion;
        public bool Authorized;
        public ulong SteamId;
    }

    internal sealed class HorusServerPrincipalState
    {
        public readonly HorusRequestDeduplicator Deduplicator = new HorusRequestDeduplicator();
        public HorusTokenBucket MutationRate;
        public HorusTokenBucket ReadRate;
        public HorusTokenBucket RejectionAuditRate;
    }
#endif
}
