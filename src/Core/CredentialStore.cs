using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MailTransfer95.Core
{
    /// <summary>
    /// Optional, opt-in credential persistence protected by Windows DPAPI
    /// (CurrentUser scope). Nothing is stored unless the user explicitly asks to
    /// remember a password for a job; the default is never to save credentials.
    ///
    /// Secrets are encrypted with the current Windows user's key, so the file cannot be
    /// read on another account or machine, and plaintext is never written to disk. OAuth
    /// tokens are deliberately NOT persisted - they are short-lived and re-acquired via
    /// the refresh token held only in memory, or by signing in again.
    /// </summary>
    public static class CredentialStore
    {
        private static readonly object Sync = new object();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MailTransfer95.DPAPI.v1");

        private static string StorePath
        {
            get { return Path.Combine(Settings.DirectoryPath, "credentials.dat"); }
        }

        public static string KeyFor(string jobName, EndpointRole role)
        {
            return "job:" + (jobName ?? string.Empty) + ":" + (role == EndpointRole.Source ? "src" : "dst");
        }

        public static void Save(string key, string secret)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(secret))
                return;

            lock (Sync)
            {
                Dictionary<string, string> map = LoadMap();
                try
                {
                    byte[] cipher = ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
                    map[key] = Convert.ToBase64String(cipher);
                    SaveMap(map);
                }
                catch
                {
                    // If encryption is unavailable we simply do not persist the secret.
                }
            }
        }

        public static string Load(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            lock (Sync)
            {
                Dictionary<string, string> map = LoadMap();
                string cipher;
                if (!map.TryGetValue(key, out cipher) || string.IsNullOrEmpty(cipher))
                    return null;

                try
                {
                    byte[] plain = ProtectedData.Unprotect(
                        Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static void Delete(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            lock (Sync)
            {
                Dictionary<string, string> map = LoadMap();
                if (map.Remove(key))
                    SaveMap(map);
            }
        }

        public static bool Has(string key)
        {
            lock (Sync)
                return LoadMap().ContainsKey(key ?? string.Empty);
        }

        private static Dictionary<string, string> LoadMap()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new Dictionary<string, string>();

                string text = File.ReadAllText(StorePath, Encoding.UTF8);
                if (string.IsNullOrEmpty(text))
                    return new Dictionary<string, string>();

                Dictionary<string, string> map =
                    new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(text);
                return map ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private static void SaveMap(Dictionary<string, string> map)
        {
            try
            {
                if (!Directory.Exists(Settings.DirectoryPath))
                    Directory.CreateDirectory(Settings.DirectoryPath);

                string text = new JavaScriptSerializer().Serialize(map);
                File.WriteAllText(StorePath, text, new UTF8Encoding(false));
            }
            catch
            {
                // Best-effort persistence.
            }
        }
    }
}
