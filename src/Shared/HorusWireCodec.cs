using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HorusMod.Shared
{
    public static class HorusWireCodec
    {
        private const uint Magic = 0x32535248; // HRS2
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(HorusPacketKind kind, object value)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(Magic);
                writer.Write((byte)kind);
                switch (kind)
                {
                    case HorusPacketKind.Hello: WriteHello(writer, (HorusHello)value); break;
                    case HorusPacketKind.Capabilities: WriteCapabilities(writer, (HorusCapabilities)value); break;
                    case HorusPacketKind.Command: WriteCommand(writer, (HorusCommandEnvelope)value); break;
                    case HorusPacketKind.CommandResult: WriteResult(writer, (HorusCommandResult)value); break;
                    case HorusPacketKind.StateRequest: WriteStateRequest(writer, (HorusStateRequest)value); break;
                    case HorusPacketKind.StatePage: WriteStatePage(writer, (HorusStatePage)value); break;
                    case HorusPacketKind.StateEvent: WriteStateEvent(writer, (HorusStateEvent)value); break;
                    default: throw new InvalidDataException("Unknown Horus packet kind.");
                }
                writer.Flush();
                if (stream.Length > HorusProtocol.MaxMessageBytes) throw new InvalidDataException("Horus packet exceeds 16 KiB.");
                return stream.ToArray();
            }
        }

        public static object Decode(byte[] data, out HorusPacketKind kind)
        {
            if (data == null || data.Length < 5 || data.Length > HorusProtocol.MaxMessageBytes)
                throw new InvalidDataException("Invalid Horus packet size.");
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream, StrictUtf8, true))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid Horus packet magic.");
                kind = (HorusPacketKind)reader.ReadByte();
                object result;
                switch (kind)
                {
                    case HorusPacketKind.Hello: result = ReadHello(reader); break;
                    case HorusPacketKind.Capabilities: result = ReadCapabilities(reader); break;
                    case HorusPacketKind.Command: result = ReadCommand(reader); break;
                    case HorusPacketKind.CommandResult: result = ReadResult(reader); break;
                    case HorusPacketKind.StateRequest: result = ReadStateRequest(reader); break;
                    case HorusPacketKind.StatePage: result = ReadStatePage(reader); break;
                    case HorusPacketKind.StateEvent: result = ReadStateEvent(reader); break;
                    default: throw new InvalidDataException("Unknown Horus packet kind.");
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Horus packet data.");
                return result;
            }
        }

        private static void WriteHello(BinaryWriter w, HorusHello v) { w.Write(v.ProtocolVersion); WriteString(w, v.ClientVersion); }
        private static HorusHello ReadHello(BinaryReader r) => new HorusHello { ProtocolVersion = r.ReadUInt16(), ClientVersion = ReadString(r) };

        private static void WriteCapabilities(BinaryWriter w, HorusCapabilities v)
        {
            w.Write(v.ProtocolVersion); WriteString(w, v.ServerVersion); WriteGuid(w, v.SessionId); w.Write(v.Revision);
            w.Write((ulong)v.Features); w.Write(v.Authorized); w.Write((ushort)v.Result); WriteString(w, v.Message);
        }
        private static HorusCapabilities ReadCapabilities(BinaryReader r) => new HorusCapabilities
        {
            ProtocolVersion = r.ReadUInt16(), ServerVersion = ReadString(r), SessionId = ReadGuid(r), Revision = r.ReadUInt64(),
            Features = (HorusCapability)r.ReadUInt64(), Authorized = r.ReadBoolean(), Result = (HorusResultCode)r.ReadUInt16(), Message = ReadString(r)
        };

        private static void WriteCommand(BinaryWriter w, HorusCommandEnvelope v)
        {
            w.Write(v.ProtocolVersion); WriteGuid(w, v.SessionId); w.Write(v.ExpectedRevision); WriteGuid(w, v.RequestId);
            w.Write((ushort)v.Command); WritePayload(w, v.Payload);
        }
        private static HorusCommandEnvelope ReadCommand(BinaryReader r) => new HorusCommandEnvelope
        {
            ProtocolVersion = r.ReadUInt16(), SessionId = ReadGuid(r), ExpectedRevision = r.ReadUInt64(), RequestId = ReadGuid(r),
            Command = (HorusCommandKind)r.ReadUInt16(), Payload = ReadPayload(r)
        };

        private static void WritePayload(BinaryWriter w, HorusCommandPayload v)
        {
            WriteUIntList(w, v.UnitIds); WriteVectorList(w, v.Points); WriteStringList(w, v.MountKeys);
            w.Write(v.TargetUnitId); WriteString(w, v.DefinitionKey); WriteString(w, v.SecondaryKey); WriteString(w, v.FactoryId);
            WriteString(w, v.PresetName); WriteString(w, v.UniqueName); w.Write(v.FactionIndex); w.Write(v.IntValue);
            w.Write(v.FloatValue); w.Write(v.FloatValue2); w.Write(v.FloatValue3); w.Write(v.Yaw); w.Write(v.BoolValue);
        }
        private static HorusCommandPayload ReadPayload(BinaryReader r)
        {
            var value = new HorusCommandPayload();
            ReadUIntList(r, value.UnitIds, HorusProtocol.MaxEntitiesPerCommand);
            ReadVectorList(r, value.Points, HorusProtocol.MaxWaypointsPerCommand);
            ReadStringList(r, value.MountKeys, HorusProtocol.MaxMounts);
            value.TargetUnitId = r.ReadUInt32(); value.DefinitionKey = ReadString(r); value.SecondaryKey = ReadString(r);
            value.FactoryId = ReadString(r); value.PresetName = ReadString(r); value.UniqueName = ReadString(r);
            value.FactionIndex = r.ReadInt32(); value.IntValue = r.ReadInt32(); value.FloatValue = r.ReadSingle();
            value.FloatValue2 = r.ReadSingle(); value.FloatValue3 = r.ReadSingle(); value.Yaw = r.ReadSingle(); value.BoolValue = r.ReadBoolean();
            return value;
        }

        private static void WriteResult(BinaryWriter w, HorusCommandResult v)
        {
            WriteGuid(w, v.RequestId); w.Write((ushort)v.Command); w.Write((ushort)v.Result); WriteGuid(w, v.SessionId);
            w.Write(v.Revision); WriteString(w, v.Message); WriteUIntList(w, v.AffectedUnitIds);
        }
        private static HorusCommandResult ReadResult(BinaryReader r)
        {
            var value = new HorusCommandResult { RequestId = ReadGuid(r), Command = (HorusCommandKind)r.ReadUInt16(), Result = (HorusResultCode)r.ReadUInt16(), SessionId = ReadGuid(r), Revision = r.ReadUInt64(), Message = ReadString(r) };
            ReadUIntList(r, value.AffectedUnitIds, HorusProtocol.MaxEntitiesPerCommand);
            return value;
        }

        private static void WriteStateRequest(BinaryWriter w, HorusStateRequest v) { WriteGuid(w, v.SessionId); w.Write(v.KnownRevision); }
        private static HorusStateRequest ReadStateRequest(BinaryReader r) => new HorusStateRequest { SessionId = ReadGuid(r), KnownRevision = r.ReadUInt64() };

        private static void WriteStatePage(BinaryWriter w, HorusStatePage v)
        {
            WriteGuid(w, v.SessionId); WriteGuid(w, v.SnapshotId); w.Write(v.Revision); w.Write(v.PageIndex); w.Write(v.PageCount); w.Write(v.RtsMode); w.Write(v.RtsDeployMode);
            WriteCount(w, v.Units.Count, 64);
            foreach (HorusUnitState unit in v.Units)
            {
                w.Write(unit.UnitId); WriteString(w, unit.DefinitionKey); WriteString(w, unit.Name); w.Write(unit.FactionIndex);
                WriteVector(w, unit.Position); w.Write(unit.HorusOwned);
            }
            WriteCount(w, v.Factories.Count, 64);
            foreach (HorusFactoryState factory in v.Factories)
            {
                WriteString(w, factory.FactoryId); WriteString(w, factory.PresetName); w.Write(factory.FactionIndex);
                w.Write(factory.Enabled); w.Write(factory.ProductionEnabled); w.Write(factory.ConsumesBudget); WriteVector(w, factory.Position);
                w.Write(factory.Yaw);w.Write(factory.GeneratesIncome);w.Write(factory.IncomePerMinute);WriteStringList(w,factory.ProductionKeys);
                w.Write(factory.CurrentProductionIndex);w.Write(factory.ProductionIntervalSeconds);w.Write(factory.ProductionTimer);
                w.Write(factory.MaxActiveProducedUnits);w.Write(factory.UsesRallyPoint);WriteVector(w,factory.RallyPoint);
                w.Write(factory.SpawnRadius);WriteString(w,factory.LastStatus);
            }
            WriteCount(w, v.Budgets.Count, 64);
            foreach (HorusBudgetState budget in v.Budgets) { w.Write(budget.FactionIndex); w.Write(budget.Budget);w.Write(budget.IncomePerTick);w.Write(budget.UnitCap);w.Write(budget.ActiveUnitCount); }
        }
        private static HorusStatePage ReadStatePage(BinaryReader r)
        {
            var value = new HorusStatePage { SessionId = ReadGuid(r), SnapshotId = ReadGuid(r), Revision = r.ReadUInt64(), PageIndex = r.ReadInt32(), PageCount = r.ReadInt32(), RtsMode = r.ReadInt32(), RtsDeployMode = r.ReadInt32() };
            int units = ReadCount(r, 64);
            for (int i = 0; i < units; i++) value.Units.Add(new HorusUnitState { UnitId = r.ReadUInt32(), DefinitionKey = ReadString(r), Name = ReadString(r), FactionIndex = r.ReadInt32(), Position = ReadVector(r), HorusOwned = r.ReadBoolean() });
            int factories = ReadCount(r, 64);
            for (int i = 0; i < factories; i++)
            {
                var factory=new HorusFactoryState { FactoryId = ReadString(r), PresetName = ReadString(r), FactionIndex = r.ReadInt32(), Enabled = r.ReadBoolean(), ProductionEnabled = r.ReadBoolean(), ConsumesBudget = r.ReadBoolean(), Position = ReadVector(r),Yaw=r.ReadSingle(),GeneratesIncome=r.ReadBoolean(),IncomePerMinute=r.ReadSingle() };
                ReadStringList(r,factory.ProductionKeys,HorusProtocol.MaxMounts);factory.CurrentProductionIndex=r.ReadInt32();factory.ProductionIntervalSeconds=r.ReadSingle();factory.ProductionTimer=r.ReadSingle();factory.MaxActiveProducedUnits=r.ReadInt32();factory.UsesRallyPoint=r.ReadBoolean();factory.RallyPoint=ReadVector(r);factory.SpawnRadius=r.ReadSingle();factory.LastStatus=ReadString(r);value.Factories.Add(factory);
            }
            int budgets = ReadCount(r, 64);
            for (int i = 0; i < budgets; i++) value.Budgets.Add(new HorusBudgetState { FactionIndex = r.ReadInt32(), Budget = r.ReadSingle(),IncomePerTick=r.ReadSingle(),UnitCap=r.ReadInt32(),ActiveUnitCount=r.ReadInt32() });
            return value;
        }

        private static void WriteStateEvent(BinaryWriter w, HorusStateEvent v) { WriteGuid(w, v.SessionId); w.Write(v.Revision); WriteResult(w, v.Result); }
        private static HorusStateEvent ReadStateEvent(BinaryReader r) => new HorusStateEvent { SessionId = ReadGuid(r), Revision = r.ReadUInt64(), Result = ReadResult(r) };

        private static void WriteVector(BinaryWriter w, HorusVector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        private static HorusVector3 ReadVector(BinaryReader r) => new HorusVector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        private static void WriteVectorList(BinaryWriter w, List<HorusVector3> values) { WriteCount(w, values.Count, HorusProtocol.MaxWaypointsPerCommand); foreach (HorusVector3 value in values) WriteVector(w, value); }
        private static void ReadVectorList(BinaryReader r, List<HorusVector3> values, int max) { int count = ReadCount(r, max); for (int i = 0; i < count; i++) values.Add(ReadVector(r)); }
        private static void WriteUIntList(BinaryWriter w, List<uint> values) { WriteCount(w, values.Count, HorusProtocol.MaxEntitiesPerCommand); foreach (uint value in values) w.Write(value); }
        private static void ReadUIntList(BinaryReader r, List<uint> values, int max) { int count = ReadCount(r, max); for (int i = 0; i < count; i++) values.Add(r.ReadUInt32()); }
        private static void WriteStringList(BinaryWriter w, List<string> values) { WriteCount(w, values.Count, HorusProtocol.MaxMounts); foreach (string value in values) WriteString(w, value); }
        private static void ReadStringList(BinaryReader r, List<string> values, int max) { int count = ReadCount(r, max); for (int i = 0; i < count; i++) values.Add(ReadString(r)); }
        private static void WriteCount(BinaryWriter w, int count, int max) { if (count < 0 || count > max) throw new InvalidDataException("Collection limit exceeded."); w.Write((ushort)count); }
        private static int ReadCount(BinaryReader r, int max) { int count = r.ReadUInt16(); if (count > max) throw new InvalidDataException("Collection limit exceeded."); return count; }
        private static void WriteGuid(BinaryWriter w, Guid value) { w.Write(value.ToByteArray()); }
        private static Guid ReadGuid(BinaryReader r) { byte[] bytes = r.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); return new Guid(bytes); }
        private static void WriteString(BinaryWriter w, string value)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? "");
            if (bytes.Length > HorusProtocol.MaxStringBytes) throw new InvalidDataException("String limit exceeded.");
            w.Write((ushort)bytes.Length); w.Write(bytes);
        }
        private static string ReadString(BinaryReader r)
        {
            int length = r.ReadUInt16();
            if (length > HorusProtocol.MaxStringBytes) throw new InvalidDataException("String limit exceeded.");
            byte[] bytes = r.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            return StrictUtf8.GetString(bytes);
        }
    }
}
