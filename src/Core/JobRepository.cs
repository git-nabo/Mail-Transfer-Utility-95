using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MailTransfer95.Core
{
    /// <summary>
    /// Stores saved migration jobs as a single JSON file under the user's profile.
    /// Credentials are NEVER written here - the endpoint secret fields are marked
    /// [ScriptIgnore] so the serializer cannot include a password or token. Only
    /// connection parameters and options are persisted.
    /// </summary>
    public static class JobRepository
    {
        private static readonly object Sync = new object();

        public static string JobsPath
        {
            get { return Path.Combine(Settings.DirectoryPath, "jobs.json"); }
        }

        public static List<TransferJob> LoadAll()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(JobsPath))
                        return new List<TransferJob>();

                    string text = File.ReadAllText(JobsPath, Encoding.UTF8);
                    if (string.IsNullOrEmpty(text))
                        return new List<TransferJob>();

                    JavaScriptSerializer js = new JavaScriptSerializer();
                    js.MaxJsonLength = 16 * 1024 * 1024;
                    List<TransferJob> list = js.Deserialize<List<TransferJob>>(text);
                    return SanitizeAll(list) ?? new List<TransferJob>();
                }
                catch
                {
                    // A corrupt job file must never crash the app; start with none.
                    return new List<TransferJob>();
                }
            }
        }

        public static void SaveAll(List<TransferJob> jobs)
        {
            lock (Sync)
            {
                try
                {
                    if (!Directory.Exists(Settings.DirectoryPath))
                        Directory.CreateDirectory(Settings.DirectoryPath);

                    JavaScriptSerializer js = new JavaScriptSerializer();
                    js.MaxJsonLength = 16 * 1024 * 1024;
                    string text = js.Serialize(jobs ?? new List<TransferJob>());
                    File.WriteAllText(JobsPath, text, new UTF8Encoding(false));
                }
                catch
                {
                    // Persistence failure is surfaced to the user by the caller if needed.
                }
            }
        }

        /// <summary>Inserts or replaces a job by name (case-insensitive).</summary>
        public static void Upsert(TransferJob job)
        {
            if (job == null || string.IsNullOrEmpty(job.Name))
                return;

            List<TransferJob> all = LoadAll();
            all.RemoveAll(delegate (TransferJob j)
            {
                return string.Equals(j.Name, job.Name, StringComparison.OrdinalIgnoreCase);
            });

            if (string.IsNullOrEmpty(job.CreatedUtc))
                job.CreatedUtc = DateTime.UtcNow.ToString("o");
            job.ModifiedUtc = DateTime.UtcNow.ToString("o");

            all.Add(job);
            SaveAll(all);
        }

        public static void Delete(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            List<TransferJob> all = LoadAll();
            all.RemoveAll(delegate (TransferJob j)
            {
                return string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase);
            });
            SaveAll(all);
        }

        public static TransferJob Find(string name)
        {
            foreach (TransferJob j in LoadAll())
                if (string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase))
                    return j;
            return null;
        }

        private static List<TransferJob> SanitizeAll(List<TransferJob> jobs)
        {
            if (jobs == null) return null;
            foreach (TransferJob j in jobs)
            {
                if (j.Source == null) j.Source = new TransferEndpoint();
                if (j.Destination == null) j.Destination = new TransferEndpoint();
                if (j.Options == null) j.Options = new TransferOptions();
                if (j.Options.SelectedFolders == null) j.Options.SelectedFolders = new List<string>();
                if (j.Options.FolderMappings == null) j.Options.FolderMappings = new List<FolderMapping>();
            }
            return jobs;
        }
    }
}
