using System;
using System.Globalization;
using System.Text;

namespace HorusMod.Shared
{
    public static class HorusAuditFormatter
    {
        public static string FormatJsonLine(DateTime utc, ulong steamId, string mission, HorusCommandEnvelope command, HorusCommandResult result)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (result == null) throw new ArgumentNullException(nameof(result));
            HorusCommandPayload payload = command.Payload ?? new HorusCommandPayload();
            var builder = new StringBuilder(768);
            builder.Append("{\"utc\":\"").Append(Escape(utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))
                .Append("\",\"steamId\":\"").Append(steamId.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"mission\":\"").Append(Escape(Limit(mission, 128)))
                .Append("\",\"requestId\":\"").Append(command.RequestId.ToString("D"))
                .Append("\",\"command\":\"").Append(command.Command)
                .Append("\",\"parameters\":{")
                .Append("\"definitionKey\":\"").Append(Escape(Limit(payload.DefinitionKey, 128)))
                .Append("\",\"secondaryKey\":\"").Append(Escape(Limit(payload.SecondaryKey, 128)))
                .Append("\",\"factoryId\":\"").Append(Escape(Limit(payload.FactoryId, 128)))
                .Append("\",\"presetName\":\"").Append(Escape(Limit(payload.PresetName, 128)))
                .Append("\",\"uniqueName\":\"").Append(Escape(Limit(payload.UniqueName, 128)))
                .Append("\",\"factionIndex\":").Append(payload.FactionIndex.ToString(CultureInfo.InvariantCulture))
                .Append(",\"targetUnitId\":").Append(payload.TargetUnitId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"unitCount\":").Append(payload.UnitIds.Count.ToString(CultureInfo.InvariantCulture))
                .Append(",\"pointCount\":").Append(payload.Points.Count.ToString(CultureInfo.InvariantCulture))
                .Append(",\"mountCount\":").Append(payload.MountKeys.Count.ToString(CultureInfo.InvariantCulture))
                .Append(",\"intValue\":").Append(payload.IntValue.ToString(CultureInfo.InvariantCulture))
                .Append(",\"floatValue\":").Append(JsonNumber(payload.FloatValue))
                .Append(",\"floatValue2\":").Append(JsonNumber(payload.FloatValue2))
                .Append(",\"floatValue3\":").Append(JsonNumber(payload.FloatValue3))
                .Append(",\"yaw\":").Append(JsonNumber(payload.Yaw))
                .Append(",\"boolValue\":").Append(payload.BoolValue ? "true" : "false")
                .Append("},\"result\":\"").Append(result.Result)
                .Append("\",\"revision\":").Append(result.Revision.ToString(CultureInfo.InvariantCulture))
                .Append(",\"reason\":\"").Append(Escape(Limit(result.Message, HorusProtocol.MaxStringBytes)))
                .Append("\"}");
            return builder.ToString();
        }

        public static bool ShouldDeleteAuditFile(DateTime lastWriteUtc, DateTime utcNow, int retentionDays)
        {
            int days = Math.Max(1, retentionDays);
            return lastWriteUtc.ToUniversalTime() < utcNow.ToUniversalTime().Date.AddDays(-days);
        }

        private static string JsonNumber(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? "null"
                : value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Limit(string value, int maxCharacters)
        {
            string safe = value ?? "";
            return safe.Length <= maxCharacters ? safe : safe.Substring(0, maxCharacters);
        }

        private static string Escape(string value)
        {
            var builder = new StringBuilder((value ?? "").Length + 16);
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }
    }
}
