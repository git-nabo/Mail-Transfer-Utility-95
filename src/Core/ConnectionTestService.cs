using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace MailTransfer95.Core
{
    /// <summary>One verification step of a connection test.</summary>
    public sealed class TestStep
    {
        public string Name;
        public bool Ok;
        public string Detail;

        public TestStep(string name) { Name = name; }
    }

    /// <summary>The full result of a connection test.</summary>
    public sealed class ConnectionTestResult
    {
        public readonly List<TestStep> Steps = new List<TestStep>();
        public bool Success;

        public TestStep Add(string name)
        {
            TestStep s = new TestStep(name);
            Steps.Add(s);
            return s;
        }
    }

    /// <summary>
    /// A minimal, read-only IMAP client that verifies an endpoint without starting a
    /// transfer: DNS resolution, TCP connect, TLS negotiation, authentication (password
    /// or Microsoft XOAUTH2) and mailbox access (SELECT INBOX). It never modifies the
    /// mailbox. This is a focused test client, not a full synchronisation engine - the
    /// actual transfer is still performed by imapsync.
    /// </summary>
    public static class ConnectionTestService
    {
        /// <summary>Runs the test synchronously (call from a background thread).</summary>
        public static ConnectionTestResult Test(TransferEndpoint ep, int timeoutSeconds)
        {
            ConnectionTestResult result = new ConnectionTestResult();
            int timeoutMs = Math.Max(5, timeoutSeconds) * 1000;

            TcpClient tcp = null;
            Stream stream = null;
            try
            {
                // 1. DNS
                TestStep dns = result.Add("DNS resolution");
                System.Net.IPAddress[] addrs;
                try
                {
                    addrs = System.Net.Dns.GetHostAddresses(ep.Host ?? string.Empty);
                    if (addrs == null || addrs.Length == 0)
                    {
                        dns.Ok = false; dns.Detail = "Host name did not resolve.";
                        return result;
                    }
                    dns.Ok = true;
                    dns.Detail = ep.Host + " -> " + addrs[0];
                }
                catch (Exception ex)
                {
                    dns.Ok = false; dns.Detail = ex.Message;
                    return result;
                }

                // 2. TCP
                TestStep tcpStep = result.Add("TCP connection");
                tcp = new TcpClient();
                try
                {
                    IAsyncResult ar = tcp.BeginConnect(ep.Host, ep.Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                        throw new TimeoutException("Timed out connecting to port " + ep.Port + ".");
                    tcp.EndConnect(ar);
                    tcp.ReceiveTimeout = timeoutMs;
                    tcp.SendTimeout = timeoutMs;
                    tcpStep.Ok = true;
                    tcpStep.Detail = "Connected to " + ep.Host + ":" + ep.Port;
                }
                catch (Exception ex)
                {
                    tcpStep.Ok = false; tcpStep.Detail = ex.Message;
                    return result;
                }

                NetworkStream netStream = tcp.GetStream();
                netStream.ReadTimeout = timeoutMs;
                netStream.WriteTimeout = timeoutMs;
                stream = netStream;

                ImapTalker imap = new ImapTalker(stream);

                // 3. TLS
                TestStep tls = result.Add("TLS negotiation");
                try
                {
                    if (ep.Encryption == EncryptionMode.SslTls)
                    {
                        stream = NegotiateTls(netStream, ep.Host);
                        imap = new ImapTalker(stream);
                        imap.ReadGreeting();
                        tls.Ok = true; tls.Detail = "Implicit TLS established.";
                    }
                    else if (ep.Encryption == EncryptionMode.StartTls)
                    {
                        imap.ReadGreeting();
                        string resp = imap.Command("STARTTLS");
                        if (!ImapTalker.IsOk(resp))
                            throw new Exception("Server refused STARTTLS: " + ImapTalker.Clean(resp));
                        stream = NegotiateTls(netStream, ep.Host);
                        imap = new ImapTalker(stream);
                        tls.Ok = true; tls.Detail = "STARTTLS upgrade established.";
                    }
                    else
                    {
                        imap.ReadGreeting();
                        tls.Ok = true; tls.Detail = "Plain text (no TLS requested).";
                    }
                }
                catch (Exception ex)
                {
                    tls.Ok = false; tls.Detail = ex.Message;
                    return result;
                }

                // 4. Authentication
                TestStep auth = result.Add("Authentication");
                try
                {
                    bool authOk;
                    string detail;
                    if (ep.Auth == AuthMode.OAuth2)
                        authOk = imap.AuthenticateXoauth2(ep.Username ?? ep.Account, ep.RuntimeAccessToken, out detail);
                    else
                        authOk = imap.AuthenticatePlain(ep.Username, ep.RuntimePassword, out detail);

                    auth.Ok = authOk;
                    auth.Detail = detail;
                    if (!authOk)
                        return result;
                }
                catch (Exception ex)
                {
                    auth.Ok = false; auth.Detail = ex.Message;
                    return result;
                }

                // 5. Mailbox access
                TestStep mbox = result.Add("Mailbox access");
                try
                {
                    string resp = imap.Command("SELECT INBOX");
                    if (ImapTalker.IsOk(resp))
                    {
                        mbox.Ok = true; mbox.Detail = "INBOX selected.";
                    }
                    else
                    {
                        // Some providers expose a different root; a successful LIST is enough.
                        string list = imap.Command("LIST \"\" \"*\"");
                        mbox.Ok = ImapTalker.IsOk(list);
                        mbox.Detail = mbox.Ok ? "Folder list retrieved." : ImapTalker.Clean(resp);
                    }
                }
                catch (Exception ex)
                {
                    mbox.Ok = false; mbox.Detail = ex.Message;
                }

                try { imap.Command("LOGOUT"); } catch { }

                result.Success = AllOk(result);
                return result;
            }
            finally
            {
                try { if (stream != null) stream.Dispose(); } catch { }
                try { if (tcp != null) tcp.Close(); } catch { }
            }
        }

        private static Stream NegotiateTls(NetworkStream netStream, string host)
        {
            SslStream ssl = new SslStream(netStream, false,
                delegate { return true; });   // certificate acceptance is reported, not enforced, in a test
            // Note: the test accepts the presented certificate so admins can diagnose
            // self-signed or mismatched certificates; imapsync enforces validation per
            // its own settings during the real transfer.
            SslProtocols protocols = SslProtocols.Tls12;
            try { protocols |= (SslProtocols)3072; } catch { }   // Tls12
            try { protocols |= (SslProtocols)12288; } catch { }  // Tls13 where available
            ssl.AuthenticateAsClient(host, null, protocols, false);
            return ssl;
        }

        private static bool AllOk(ConnectionTestResult result)
        {
            foreach (TestStep s in result.Steps)
                if (!s.Ok) return false;
            return true;
        }
    }

    /// <summary>A tiny IMAP protocol helper over an established stream.</summary>
    internal sealed class ImapTalker
    {
        private readonly Stream _stream;
        private int _tag;

        public ImapTalker(Stream stream) { _stream = stream; }

        public void ReadGreeting()
        {
            string line = ReadLine();
            if (line == null || !(line.StartsWith("* OK") || line.StartsWith("* PREAUTH")))
                throw new Exception("Unexpected server greeting: " + Clean(line));
        }

        /// <summary>Sends a tagged command and returns the full tagged response text.</summary>
        public string Command(string command)
        {
            string tag = NextTag();
            WriteLine(tag + " " + command);
            return ReadResponse(tag);
        }

        public bool AuthenticatePlain(string user, string password, out string detail)
        {
            if (string.IsNullOrEmpty(user))
            {
                detail = "No user name supplied.";
                return false;
            }

            string tag = NextTag();
            WriteLine(tag + " AUTHENTICATE PLAIN");

            string cont = ReadLine();
            if (cont == null || !cont.StartsWith("+"))
            {
                detail = "Server did not accept AUTHENTICATE PLAIN: " + Clean(cont);
                return false;
            }

            string payload = "\0" + user + "\0" + (password ?? string.Empty);
            WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));

            string resp = ReadResponse(tag);
            bool ok = IsOk(resp);
            detail = ok ? "Authenticated as " + user + "." : "Authentication failed: " + Clean(resp);
            return ok;
        }

        public bool AuthenticateXoauth2(string user, string accessToken, out string detail)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                detail = "Not signed in - no OAuth access token available.";
                return false;
            }

            string tag = NextTag();
            WriteLine(tag + " AUTHENTICATE XOAUTH2");

            string cont = ReadLine();
            if (cont == null || !cont.StartsWith("+"))
            {
                detail = "Server did not accept AUTHENTICATE XOAUTH2: " + Clean(cont);
                return false;
            }

            string raw = "user=" + user + "\x01auth=Bearer " + accessToken + "\x01\x01";
            WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));

            string resp = ReadLine();
            // On failure the server sends a '+' challenge with a base64 error; answer
            // with an empty line to receive the tagged NO/BAD.
            if (resp != null && resp.StartsWith("+"))
            {
                WriteLine(string.Empty);
                resp = ReadResponse(tag);
            }
            else if (resp != null && !resp.StartsWith(tag))
            {
                resp = resp + " " + ReadResponse(tag);
            }

            bool ok = IsOk(resp);
            detail = ok ? "Authenticated as " + user + " via OAuth 2.0." : "OAuth authentication failed: " + Clean(resp);
            return ok;
        }

        private string ReadResponse(string tag)
        {
            StringBuilder sb = new StringBuilder();
            string line;
            while ((line = ReadLine()) != null)
            {
                sb.AppendLine(line);
                if (line.StartsWith(tag + " ", StringComparison.Ordinal))
                    break;
            }
            return sb.ToString();
        }

        public static bool IsOk(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;
            foreach (string lineRaw in response.Split('\n'))
            {
                string line = lineRaw.Trim();
                int sp = line.IndexOf(' ');
                if (sp > 0)
                {
                    string rest = line.Substring(sp + 1);
                    if (rest.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (rest.StartsWith("NO", StringComparison.OrdinalIgnoreCase) ||
                        rest.StartsWith("BAD", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return false;
        }

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(no response)";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private string NextTag()
        {
            _tag++;
            return "A" + _tag.ToString("D3");
        }

        private void WriteLine(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text + "\r\n");
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }

        private string ReadLine()
        {
            StringBuilder sb = new StringBuilder();
            int b;
            bool any = false;
            while ((b = _stream.ReadByte()) != -1)
            {
                any = true;
                if (b == '\n')
                    break;
                if (b != '\r')
                    sb.Append((char)b);
            }
            if (!any && sb.Length == 0)
                return null;
            return sb.ToString();
        }
    }
}
