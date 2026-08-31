using System;
using System.Globalization;
using System.IO;
using HorusMod.Shared;

namespace HorusMod.Server
{
    internal sealed class HorusAuditWriter
    {
        private readonly string directory;
        private readonly int retentionDays;
        private DateTime nextPruneUtc;

        public HorusAuditWriter(string directory, int retentionDays)
        {
            this.directory = directory;
            this.retentionDays = Math.Max(1, retentionDays);
            Directory.CreateDirectory(directory);
            Prune();
        }

        public void Write(ulong steamId, string mission, HorusCommandEnvelope command, HorusCommandResult result)
        {
            try
            {
                DateTime now=DateTime.UtcNow;if(now>=nextPruneUtc)Prune();
                string path = Path.Combine(directory, "horus-audit-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");
                string line = HorusAuditFormatter.FormatJsonLine(now, steamId, mission, command, result) + Environment.NewLine;
                File.AppendAllText(path, line, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                HorusMod.Logging.HorusLog.Warning("Audit", "Failed to write audit record: " + ex.Message);
            }
        }

        private void Prune()
        {
            try
            {
                foreach (string file in Directory.GetFiles(directory, "horus-audit-*.jsonl"))
                    if (HorusAuditFormatter.ShouldDeleteAuditFile(File.GetLastWriteTimeUtc(file), DateTime.UtcNow, retentionDays)) File.Delete(file);
            }
            catch { }
            nextPruneUtc=DateTime.UtcNow.Date.AddDays(1);
        }
    }
}
