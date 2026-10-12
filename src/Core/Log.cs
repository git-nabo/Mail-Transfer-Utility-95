using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MailTransfer95.Core
{
    /// <summary>
    /// Optional migration log. Disabled by default; writes nothing (and creates no file)
    /// until the user enables it in File -&gt; Options. It records timestamps, addresses,
    /// counts, errors and non-secret command options only - never passwords or tokens.
    /// </summary>
    public static class Log
    {
        private static readonly object Sync = new object();

        public static bool Enabled
        {
            get { return Settings.LoggingEnabled; }
        }

        public static string LogPath
        {
            get { return Path.Combine(Settings.EffectiveWorkingDirectory, "mailtransfer95.log"); }
        }

        public static void Write(string category, string message)
        {
            if (!Enabled)
                return;

            try
            {
                Settings.EnsureWorkingDirectory();

                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss}  [{1}]  {2}",
                    DateTime.Now, category ?? "-", Redaction.Scrub(message ?? string.Empty));

                lock (Sync)
                    File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never interfere with the actual migration.
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERROR", message); }
    }

    /// <summary>
    /// Centralised secret redaction. Every command preview, log line and details pane
    /// passes through here so a password or token can never leak into the UI or a file.
    /// </summary>
    public static class Redaction
    {
        public const string Mask = "********";

        /// <summary>
        /// Replaces the value following a credential-bearing imapsync switch with a mask.
        /// Handles both "--password1 secret" and "--password1=secret" forms.
        /// </summary>
        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string[] switches =
            {
                "--password1", "--password2",
                "--oauthaccesstoken1", "--oauthaccesstoken2",
                "--oauthdirectaccesstoken1", "--oauthdirectaccesstoken2"
            };

            string result = text;
            foreach (string sw in switches)
            {
                result = ScrubSwitch(result, sw);
            }
            return result;
        }

        private static string ScrubSwitch(string text, string sw)
        {
            int idx = 0;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int found = text.IndexOf(sw, idx, StringComparison.Ordinal);
                if (found < 0)
                {
                    sb.Append(text.Substring(idx));
                    break;
                }

                sb.Append(text.Substring(idx, found - idx));
                sb.Append(sw);

                int after = found + sw.Length;
                // Skip the separator ('=' or spaces) then mask the following token.
                int p = after;
                char sep = ' ';
                if (p < text.Length && (text[p] == '=' || text[p] == ' '))
                {
                    sep = text[p];
                    sb.Append(sep);
                    p++;
                }
                while (p < text.Length && text[p] == ' ') { sb.Append(' '); p++; }

                // Consume the value token (until next whitespace).
                while (p < text.Length && text[p] != ' ')
                    p++;

                sb.Append(Mask);
                idx = p;
            }
            return sb.ToString();
        }
    }
}
