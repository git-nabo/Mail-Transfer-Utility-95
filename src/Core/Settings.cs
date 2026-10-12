using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MailTransfer95.Core
{
    /// <summary>
    /// User preferences persisted as a small INI-style file under the user's profile.
    ///
    /// Nothing developer-specific is hardcoded: the imapsync path, working directory and
    /// Microsoft 365 OAuth tenant/client IDs all start empty and are filled in by the
    /// administrator. Logging is OFF by default.
    ///
    /// No secrets are ever written here - not passwords, not tokens, not client secrets.
    /// </summary>
    public static class Settings
    {
        private static readonly object Sync = new object();
        private static bool _loaded;

        /// <summary>Explicit path to imapsync.exe. Empty means "use the default search order".</summary>
        public static string ImapSyncPath { get; set; }

        /// <summary>Per-connection timeout in seconds used by connection tests and imapsync.</summary>
        public static int TimeoutSeconds { get; set; }

        /// <summary>When false, nothing is written to the log file at all.</summary>
        public static bool LoggingEnabled { get; set; }

        /// <summary>Working directory for transient run files and logs. Empty = default under LocalAppData.</summary>
        public static string WorkingDirectoryOverride { get; set; }

        /// <summary>Microsoft Entra directory (tenant) ID, or "common"/"organizations". Never hardcoded.</summary>
        public static string OAuthTenantId { get; set; }

        /// <summary>Application (client) ID of the public client app registration. Never hardcoded.</summary>
        public static string OAuthClientId { get; set; }

        public static string DirectoryPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MailTransfer95");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(DirectoryPath, "settings.ini"); }
        }

        /// <summary>The effective working directory: the override if set and usable, else the default.</summary>
        public static string EffectiveWorkingDirectory
        {
            get
            {
                string dir = WorkingDirectoryOverride;
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(DirectoryPath, "work");
                return dir;
            }
        }

        public static void EnsureWorkingDirectory()
        {
            try
            {
                string dir = EffectiveWorkingDirectory;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch
            {
                // A missing working directory is reported later at the point of use.
            }
        }

        public static void Load()
        {
            lock (Sync)
            {
                if (_loaded)
                    return;

                _loaded = true;

                // Defaults.
                ImapSyncPath = string.Empty;
                TimeoutSeconds = 60;
                LoggingEnabled = false;
                WorkingDirectoryOverride = string.Empty;
                OAuthTenantId = "common";
                OAuthClientId = string.Empty;

                try
                {
                    if (!File.Exists(SettingsPath))
                        return;

                    foreach (string raw in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                            continue;

                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                            continue;

                        string key = line.Substring(0, eq).Trim();
                        string value = line.Substring(eq + 1).Trim();

                        ApplySetting(key, value);
                    }
                }
                catch
                {
                    // Corrupt settings must never stop the app from starting.
                }
            }
        }

        private static void ApplySetting(string key, string value)
        {
            if (Eq(key, "ImapSyncPath")) ImapSyncPath = value;
            else if (Eq(key, "TimeoutSeconds"))
            {
                int n;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                    TimeoutSeconds = Math.Min(3600, n);
            }
            else if (Eq(key, "LoggingEnabled"))
            {
                bool b;
                LoggingEnabled = bool.TryParse(value, out b) && b;
            }
            else if (Eq(key, "WorkingDirectory")) WorkingDirectoryOverride = value;
            else if (Eq(key, "OAuthTenantId")) OAuthTenantId = value;
            else if (Eq(key, "OAuthClientId")) OAuthClientId = value;
        }

        private static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static void Save()
        {
            lock (Sync)
            {
                try
                {
                    if (!Directory.Exists(DirectoryPath))
                        Directory.CreateDirectory(DirectoryPath);

                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("# Mail Transfer Utility 95 settings");
                    sb.AppendLine("# This file never stores passwords, OAuth tokens or client secrets.");
                    sb.AppendLine("ImapSyncPath=" + (ImapSyncPath ?? string.Empty));
                    sb.AppendLine("TimeoutSeconds=" + TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("LoggingEnabled=" + LoggingEnabled.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("WorkingDirectory=" + (WorkingDirectoryOverride ?? string.Empty));
                    sb.AppendLine("OAuthTenantId=" + (OAuthTenantId ?? string.Empty));
                    sb.AppendLine("OAuthClientId=" + (OAuthClientId ?? string.Empty));

                    File.WriteAllText(SettingsPath, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // If we cannot persist the preference the app still keeps running.
                }
            }
        }
    }
}
