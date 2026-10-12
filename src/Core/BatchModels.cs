using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MailTransfer95.Core
{
    /// <summary>
    /// One mailbox in a batch run. Credentials live only in memory for the duration of
    /// the batch and are never written to disk (no job file, no settings, no log).
    /// </summary>
    public sealed class BatchAccount
    {
        public bool Enabled = true;
        public string SourceUser;
        public string SourcePassword;
        public string DestUser;
        public string DestPassword;

        // OAuth tokens acquired at run time for a Microsoft 365 side (in-memory only).
        public string SourceAccessToken;
        public string SourceRefreshToken;
        public DateTime SourceTokenExpiresUtc;
        public string DestAccessToken;
        public string DestRefreshToken;
        public DateTime DestTokenExpiresUtc;

        public string Status = "Ready";
        public MigrationReport LastReport;

        public string EffectiveDestUser
        {
            get { return string.IsNullOrEmpty(DestUser) ? SourceUser : DestUser; }
        }
    }

    /// <summary>
    /// The shared configuration taken from the main window and applied to every account
    /// in a batch: direction, both server templates (host/port/encryption/auth, no
    /// credentials) and the transfer options.
    /// </summary>
    public sealed class BatchContext
    {
        public TransferType Type;
        public TransferEndpoint SourceTemplate;
        public TransferEndpoint DestTemplate;
        public TransferOptions Options;
        public string JobBaseName;

        public bool SourceIsOAuth { get { return SourceTemplate != null && SourceTemplate.Auth == AuthMode.OAuth2; } }
        public bool DestIsOAuth { get { return DestTemplate != null && DestTemplate.Auth == AuthMode.OAuth2; } }

        /// <summary>Builds a full TransferJob for one account from the shared templates.</summary>
        public TransferJob BuildJob(BatchAccount a)
        {
            TransferJob job = new TransferJob();
            job.Type = Type;
            job.Options = Options != null ? Options.Clone() : new TransferOptions();
            job.Source = SourceTemplate.Clone();
            job.Destination = DestTemplate.Clone();

            // Source credentials.
            job.Source.Username = a.SourceUser;
            if (job.Source.Auth == AuthMode.OAuth2)
            {
                job.Source.Account = a.SourceUser;
                job.Source.RuntimeAccessToken = a.SourceAccessToken;
                job.Source.RuntimeRefreshToken = a.SourceRefreshToken;
            }
            else
            {
                job.Source.RuntimePassword = a.SourcePassword;
            }

            // Destination credentials (default to the source user when none is given).
            string destUser = a.EffectiveDestUser;
            job.Destination.Username = destUser;
            if (job.Destination.Auth == AuthMode.OAuth2)
            {
                job.Destination.Account = destUser;
                job.Destination.RuntimeAccessToken = a.DestAccessToken;
                job.Destination.RuntimeRefreshToken = a.DestRefreshToken;
            }
            else
            {
                string destPass = a.DestPassword;
                if (string.IsNullOrEmpty(destPass) && string.IsNullOrEmpty(a.DestUser))
                    destPass = a.SourcePassword;   // same mailbox on both sides
                job.Destination.RuntimePassword = destPass;
            }

            job.Name = (string.IsNullOrEmpty(JobBaseName) ? "Batch" : JobBaseName) + " - " + a.SourceUser;
            return job;
        }

        /// <summary>Returns a validation message for an account, or null when it is runnable.</summary>
        public string Validate(BatchAccount a)
        {
            if (string.IsNullOrEmpty(a.SourceUser))
                return "Source user is empty.";
            if (!SourceIsOAuth && string.IsNullOrEmpty(a.SourcePassword))
                return "Source password is empty.";
            if (!DestIsOAuth)
            {
                string destPass = a.DestPassword;
                if (string.IsNullOrEmpty(destPass) && string.IsNullOrEmpty(a.DestUser))
                    destPass = a.SourcePassword;
                if (string.IsNullOrEmpty(destPass))
                    return "Destination password is empty.";
            }
            return null;
        }
    }

    /// <summary>
    /// Minimal CSV import/export for batch accounts. Columns:
    /// SourceUser, SourcePassword, DestUser, DestPassword (the last two optional).
    /// Passwords in a CSV are plain text - the UI warns about this and nothing is stored.
    /// </summary>
    public static class BatchCsv
    {
        public const string TemplateHeader = "SourceUser,SourcePassword,DestUser,DestPassword";

        public static string Template()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(TemplateHeader);
            sb.AppendLine("alice@oldprovider.example,SOURCE_PASSWORD,alice@newdomain.example,DEST_PASSWORD");
            sb.AppendLine("bob@oldprovider.example,SOURCE_PASSWORD,,");
            return sb.ToString();
        }

        public static List<BatchAccount> Parse(string text, out string error)
        {
            error = null;
            List<BatchAccount> list = new List<BatchAccount>();
            if (string.IsNullOrEmpty(text))
            {
                error = "The file is empty.";
                return list;
            }

            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            bool first = true;
            foreach (string raw in lines)
            {
                string line = raw;
                if (line == null) continue;
                if (line.Trim().Length == 0) continue;

                List<string> cols = SplitCsvLine(line);

                // Skip a header row if present.
                if (first)
                {
                    first = false;
                    if (cols.Count > 0 &&
                        cols[0].Trim().Equals("SourceUser", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (cols.Count == 0 || cols[0].Trim().Length == 0)
                    continue;

                BatchAccount a = new BatchAccount();
                a.SourceUser = Get(cols, 0);
                a.SourcePassword = Get(cols, 1);
                a.DestUser = Get(cols, 2);
                a.DestPassword = Get(cols, 3);
                list.Add(a);
            }

            if (list.Count == 0)
                error = "No account rows were found in the file.";
            return list;
        }

        private static string Get(List<string> cols, int i)
        {
            return (i < cols.Count) ? cols[i].Trim() : string.Empty;
        }

        private static List<string> SplitCsvLine(string line)
        {
            List<string> result = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { result.Add(cur.ToString()); cur.Length = 0; }
                    else cur.Append(c);
                }
            }
            result.Add(cur.ToString());
            return result;
        }
    }
}
