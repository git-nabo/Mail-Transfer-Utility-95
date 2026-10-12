using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace MailTransfer95.Core
{
    /// <summary>The outcome of listing an endpoint's folders.</summary>
    public sealed class FolderListResult
    {
        public bool Success;
        public string Error;
        public readonly List<string> Folders = new List<string>();
    }

    /// <summary>
    /// Connects to an endpoint and lists its IMAP folders (read-only) so the user can
    /// pick a subset or build folder mappings. Reuses the same minimal IMAP client as
    /// the connection test; it never modifies the mailbox.
    /// </summary>
    public static class FolderService
    {
        private static readonly Regex RxList =
            new Regex("^\\*\\s+LIST\\s+\\(([^)]*)\\)\\s+(\"[^\"]*\"|NIL|\\S+)\\s+(.+)$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static FolderListResult ListFolders(TransferEndpoint ep, int timeoutSeconds)
        {
            FolderListResult result = new FolderListResult();
            int timeoutMs = Math.Max(5, timeoutSeconds) * 1000;

            TcpClient tcp = null;
            Stream stream = null;
            try
            {
                tcp = new TcpClient();
                IAsyncResult ar = tcp.BeginConnect(ep.Host, ep.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    result.Error = "Timed out connecting to " + ep.Host + ":" + ep.Port + ".";
                    return result;
                }
                tcp.EndConnect(ar);
                tcp.ReceiveTimeout = timeoutMs;
                tcp.SendTimeout = timeoutMs;

                NetworkStream net = tcp.GetStream();
                net.ReadTimeout = timeoutMs;
                net.WriteTimeout = timeoutMs;
                stream = net;

                ImapTalker imap;
                if (ep.Encryption == EncryptionMode.SslTls)
                {
                    stream = Tls(net, ep.Host);
                    imap = new ImapTalker(stream);
                    imap.ReadGreeting();
                }
                else if (ep.Encryption == EncryptionMode.StartTls)
                {
                    imap = new ImapTalker(stream);
                    imap.ReadGreeting();
                    if (!ImapTalker.IsOk(imap.Command("STARTTLS")))
                    {
                        result.Error = "Server refused STARTTLS.";
                        return result;
                    }
                    stream = Tls(net, ep.Host);
                    imap = new ImapTalker(stream);
                }
                else
                {
                    imap = new ImapTalker(stream);
                    imap.ReadGreeting();
                }

                string detail;
                bool authOk = ep.Auth == AuthMode.OAuth2
                    ? imap.AuthenticateXoauth2(ep.Username ?? ep.Account, ep.RuntimeAccessToken, out detail)
                    : imap.AuthenticatePlain(ep.Username, ep.RuntimePassword, out detail);

                if (!authOk)
                {
                    result.Error = detail;
                    return result;
                }

                string resp = imap.Command("LIST \"\" \"*\"");
                foreach (string lineRaw in resp.Split('\n'))
                {
                    string line = lineRaw.Trim();
                    Match m = RxList.Match(line);
                    if (!m.Success)
                        continue;

                    string flags = m.Groups[1].Value;
                    if (flags.IndexOf("\\Noselect", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    string name = m.Groups[3].Value.Trim();
                    if (name.StartsWith("\"") && name.EndsWith("\"") && name.Length >= 2)
                        name = name.Substring(1, name.Length - 2);
                    name = name.Replace("\\\"", "\"").Replace("\\\\", "\\");

                    if (name.Length > 0 && !result.Folders.Contains(name))
                        result.Folders.Add(name);
                }

                try { imap.Command("LOGOUT"); } catch { }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                try { if (stream != null) stream.Dispose(); } catch { }
                try { if (tcp != null) tcp.Close(); } catch { }
            }
        }

        private static Stream Tls(NetworkStream net, string host)
        {
            SslStream ssl = new SslStream(net, false, delegate { return true; });
            SslProtocols protocols = (SslProtocols)3072;
            try { protocols |= (SslProtocols)12288; } catch { }
            ssl.AuthenticateAsClient(host, null, protocols, false);
            return ssl;
        }
    }
}
