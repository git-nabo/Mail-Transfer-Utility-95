using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MailTransfer95.Core
{
    /// <summary>A completed migration's summary, suitable for display and saving.</summary>
    public sealed class MigrationReport
    {
        public TransferJob Job;
        public TransferProgress Progress;
        public int ExitCode;
        public bool Success;
        public bool Cancelled;
        public TimeSpan Duration;
        public string ExitExplanation;
        public bool DryRun;

        public string SourceAddress
        {
            get { return Describe(Job != null ? Job.Source : null); }
        }

        public string DestinationAddress
        {
            get { return Describe(Job != null ? Job.Destination : null); }
        }

        private static string Describe(TransferEndpoint ep)
        {
            if (ep == null) return "(unknown)";
            string user = !string.IsNullOrEmpty(ep.Account) ? ep.Account : ep.Username;
            if (string.IsNullOrEmpty(user)) user = ep.Host;
            return user + (string.IsNullOrEmpty(ep.Host) ? string.Empty : "  (" + ep.Host + ")");
        }

        public string HeadlineText
        {
            get
            {
                if (Cancelled) return "Transfer cancelled.";
                if (!Success) return "Transfer completed with errors.";
                if (DryRun) return "Dry run completed (no changes were made).";
                return "Transfer completed.";
            }
        }

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Mail Transfer Utility 95 - Migration Report");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine(HeadlineText);
            sb.AppendLine();
            if (Job != null)
            {
                sb.AppendLine("Direction:   " + Job.DirectionText());
                if (!string.IsNullOrEmpty(Job.Name))
                    sb.AppendLine("Job:         " + Job.Name);
            }
            sb.AppendLine("Source:      " + SourceAddress);
            sb.AppendLine("Destination: " + DestinationAddress);
            sb.AppendLine();

            TransferProgress p = Progress ?? new TransferProgress();
            if (p.TotalFolders > 0)
                sb.AppendLine("Folders:          " + p.TotalFolders);
            sb.AppendLine("Messages copied:  " + p.CopiedMessages);
            sb.AppendLine("Messages skipped: " + p.SkippedMessages);
            sb.AppendLine("Errors:           " + p.ErrorCount);
            if (p.BytesTransferred > 0)
                sb.AppendLine("Data transferred: " + FormatBytes(p.BytesTransferred));
            sb.AppendLine("Duration:         " + FormatDuration(Duration));
            sb.AppendLine();
            sb.AppendLine("Result: " + ExitExplanation);
            if (DryRun)
                sb.AppendLine("NOTE: This was a simulation. No mailbox changes were made.");
            return sb.ToString();
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return v.ToString(u == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[u];
        }

        public static string FormatDuration(TimeSpan d)
        {
            if (d.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
                    (int)d.TotalHours, d.Minutes, d.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", d.Minutes, d.Seconds);
        }
    }

    /// <summary>
    /// Turns imapsync exit codes and run state into a readable report. Known exit codes
    /// are translated into plain language rather than "Process exited with code N".
    /// </summary>
    public static class ReportService
    {
        public static MigrationReport Build(TransferJob job, TransferProgress progress,
                                            ImapSyncRunResult run, TimeSpan duration)
        {
            MigrationReport r = new MigrationReport();
            r.Job = job;
            r.Progress = progress ?? new TransferProgress();
            r.ExitCode = run != null ? run.ExitCode : -1;
            r.Cancelled = run != null && run.Cancelled;
            r.Success = run != null && run.Success;
            r.Duration = duration;
            r.DryRun = job != null && job.Options != null && job.Options.DryRun;

            if (run != null && !string.IsNullOrEmpty(run.ErrorMessage))
                r.ExitExplanation = run.ErrorMessage;
            else if (r.Cancelled)
                r.ExitExplanation = "Cancelled by the user. The destination mailbox may be only " +
                                    "partially synchronised; re-run the job to continue.";
            else
                r.ExitExplanation = ExplainExitCode(r.ExitCode, r.Progress);

            return r;
        }

        /// <summary>Translates an imapsync exit code into a readable explanation.</summary>
        public static string ExplainExitCode(int code, TransferProgress progress)
        {
            switch (code)
            {
                case 0:
                    return "imapsync finished successfully.";
                case 8:
                    return "Another imapsync run appears to be active, or its lock/PID file " +
                           "could not be created (exit 8).";
                case 10:
                    return "A connection to one of the servers failed (exit 10). Check the host, " +
                           "port and network reachability.";
                case 12:
                    return "TLS negotiation failed (exit 12). Check the encryption setting and the " +
                           "server certificate.";
                case 16:
                    return "Authentication failed (exit 16). Check the user name and password, or " +
                           "the OAuth sign-in for Microsoft 365.";
                case 21:
                    return "A mailbox folder could not be selected (exit 21).";
                case 111:
                    return "The transfer finished but some messages reported errors (exit 111). " +
                           "See the details for the affected items.";
                case 112:
                    return "The transfer stopped after reaching the maximum error count (exit 112).";
                case 113:
                    return "A transfer limit was exceeded (exit 113).";
                case -1:
                    return "imapsync did not return an exit code; the process may have been " +
                           "terminated.";
                default:
                    long errs = progress != null ? progress.ErrorCount : 0;
                    if (errs > 0)
                        return "imapsync reported " + errs + " error(s) (exit code " + code +
                               "). See the details output for the specific messages.";
                    return "imapsync exited with code " + code +
                           ". See the details output for the specific messages.";
            }
        }

        public static string SaveReport(MigrationReport report, string suggestedName)
        {
            Settings.EnsureWorkingDirectory();
            string dir = Settings.EffectiveWorkingDirectory;
            string name = string.IsNullOrEmpty(suggestedName) ? "migration-report" : suggestedName;
            string safe = MakeSafeFileName(name) + "-" +
                          DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";
            string path = Path.Combine(dir, safe);
            File.WriteAllText(path, report.ToText(), new UTF8Encoding(false));
            return path;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
