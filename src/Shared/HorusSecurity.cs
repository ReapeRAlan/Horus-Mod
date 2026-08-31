using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HorusMod.Shared
{
    public sealed class HorusAdminAllowlist
    {
        private const ulong IndividualSteamId64Prefix = 76561197960265728UL;
        private readonly HashSet<ulong> steamIds = new HashSet<ulong>();

        public int Count => steamIds.Count;
        public bool Contains(ulong steamId) => steamId != 0 && steamIds.Contains(steamId);

        public static HorusAdminAllowlist Parse(IEnumerable<string> lines, out List<string> errors)
        {
            var result = new HorusAdminAllowlist();
            errors = new List<string>();
            if (lines == null) return result;

            int lineNumber = 0;
            foreach (string sourceLine in lines)
            {
                lineNumber++;
                string line = sourceLine ?? "";
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line.Substring(0, comment);
                line = line.Trim();
                if (line.Length == 0) continue;

                if (!ulong.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || !IsIndividualSteamId64(id))
                {
                    errors.Add("Line " + lineNumber + " is not a valid individual SteamID64.");
                    continue;
                }
                result.steamIds.Add(id);
            }
            return errors.Count == 0 ? result : new HorusAdminAllowlist();
        }

        public static bool IsIndividualSteamId64(ulong id)
        {
            if (id <= IndividualSteamId64Prefix) return false;
            ulong universe = id >> 56;
            ulong accountType = (id >> 52) & 0xFUL;
            ulong instance = (id >> 32) & 0xFFFFFUL;
            return universe == 1UL && accountType == 1UL && instance == 1UL;
        }
    }

    public sealed class HorusTokenBucket
    {
        private readonly double ratePerSecond;
        private readonly double capacity;
        private double tokens;
        private double lastTime;

        public HorusTokenBucket(double ratePerSecond, double capacity, double now)
        {
            if (ratePerSecond <= 0 || capacity <= 0) throw new ArgumentOutOfRangeException();
            this.ratePerSecond = ratePerSecond;
            this.capacity = capacity;
            tokens = capacity;
            lastTime = now;
        }

        public bool TryConsume(double now, double amount = 1d)
        {
            if (amount <= 0 || amount > capacity) return false;
            if (now > lastTime)
            {
                tokens = Math.Min(capacity, tokens + (now - lastTime) * ratePerSecond);
                lastTime = now;
            }
            if (tokens < amount) return false;
            tokens -= amount;
            return true;
        }
    }

    public sealed class HorusRequestDeduplicator
    {
        private readonly Dictionary<Guid, double> seen = new Dictionary<Guid, double>();
        private readonly Queue<Guid> order = new Queue<Guid>();
        private readonly int capacity;
        private readonly double retentionSeconds;

        public HorusRequestDeduplicator(int capacity = 2048, double retentionSeconds = 600d)
        {
            if (capacity < 1 || retentionSeconds <= 0) throw new ArgumentOutOfRangeException();
            this.capacity = capacity;
            this.retentionSeconds = retentionSeconds;
        }

        public bool TryRemember(Guid requestId, double now)
        {
            if (requestId == Guid.Empty) return false;
            Prune(now);
            if (seen.ContainsKey(requestId)) return false;
            seen[requestId] = now;
            order.Enqueue(requestId);
            Prune(now);
            return true;
        }

        private void Prune(double now)
        {
            while (order.Count > 0)
            {
                Guid id = order.Peek();
                if (!seen.TryGetValue(id, out double timestamp))
                {
                    order.Dequeue();
                    continue;
                }
                if (seen.Count <= capacity && now - timestamp <= retentionSeconds) break;
                order.Dequeue();
                seen.Remove(id);
            }
        }
    }

    public static class HorusCommandValidator
    {
        public static bool TryValidate(HorusCommandEnvelope envelope, out string error)
        {
            error = "";
            if (envelope == null || envelope.Payload == null) return Fail("Command payload is missing.", out error);
            if (envelope.ProtocolVersion != HorusProtocol.Version) return Fail("Protocol version mismatch.", out error);
            if (envelope.RequestId == Guid.Empty) return Fail("RequestId is required.", out error);
            if (envelope.Command == HorusCommandKind.None) return Fail("Command kind is required.", out error);
            if (!Enum.IsDefined(typeof(HorusCommandKind), envelope.Command)) return Fail("Unknown command kind.", out error);
            if (envelope.Payload.UnitIds.Count > HorusProtocol.MaxEntitiesPerCommand) return Fail("Too many unit ids.", out error);
            if (envelope.Payload.Points.Count > HorusProtocol.MaxWaypointsPerCommand) return Fail("Too many points.", out error);
            if (envelope.Payload.MountKeys.Count > HorusProtocol.MaxMounts) return Fail("Too many mounts.", out error);
            if (!ValidKey(envelope.Payload.DefinitionKey) || !ValidKey(envelope.Payload.SecondaryKey) ||
                !ValidKey(envelope.Payload.FactoryId) || !ValidKey(envelope.Payload.PresetName) ||
                !ValidKey(envelope.Payload.UniqueName))
                return Fail("Text values contain unsupported control characters.", out error);
            var unitIds=new HashSet<uint>();
            foreach(uint unitId in envelope.Payload.UnitIds)
                if(unitId==0||!unitIds.Add(unitId))return Fail("Unit ids must be nonzero and unique.",out error);
            foreach (string mountKey in envelope.Payload.MountKeys)
                if (!ValidKey(mountKey)) return Fail("Mount keys contain unsupported control characters.", out error);
            int mountBytes = 0;
            foreach (string mountKey in envelope.Payload.MountKeys)
            {
                try { mountBytes = checked(mountBytes + Encoding.UTF8.GetByteCount(mountKey)); }
                catch (Exception ex) when (ex is EncoderFallbackException || ex is OverflowException)
                { return Fail("Mount keys are not valid bounded UTF-8.", out error); }
                if (mountBytes > HorusProtocol.MaxStringListBytes) return Fail("Mount key byte limit exceeded.", out error);
            }
            foreach (HorusVector3 point in envelope.Payload.Points)
                if (!point.IsFinite || Math.Abs(point.X) > 100000000f || Math.Abs(point.Y) > 100000000f || Math.Abs(point.Z) > 100000000f)
                    return Fail("Coordinates must be finite and within supported world bounds.", out error);
            if (!Finite(envelope.Payload.FloatValue) || !Finite(envelope.Payload.FloatValue2) ||
                !Finite(envelope.Payload.FloatValue3) || !Finite(envelope.Payload.Yaw))
                return Fail("Numeric values must be finite.", out error);
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidKey(string value)
        {
            return HorusWireText.IsStableKey(value);
        }
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
