using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MailTransfer95.Core
{
    /// <summary>The device-code challenge the user must complete in a browser.</summary>
    public sealed class DeviceCodeInfo
    {
        public string UserCode;
        public string DeviceCode;
        public string VerificationUri;
        public int ExpiresInSeconds;
        public int IntervalSeconds;
        public string Message;
    }

    /// <summary>The result of acquiring (or failing to acquire) a token.</summary>
    public sealed class TokenResult
    {
        public bool Success;
        public string AccessToken;
        public string RefreshToken;
        public DateTime ExpiresUtc;
        public string Account;
        public string Error;
        public bool Cancelled;
    }

    /// <summary>
    /// Microsoft identity platform (Entra ID) authentication for Exchange Online IMAP,
    /// using the OAuth 2.0 device authorization grant. This is the recommended flow for
    /// a headless/desktop admin utility: the administrator signs in on the Microsoft
    /// web page, so this application never sees the password.
    ///
    /// It is a public client (native app) flow, so no client secret is involved and
    /// nothing secret is ever stored. The tenant and client IDs come from the user's own
    /// app registration configured in Tools -&gt; OAuth Settings; none are hardcoded.
    /// </summary>
    public static class OAuthService
    {
        /// <summary>Delegated scope granting XOAUTH2 IMAP access to Exchange Online.</summary>
        public const string ImapScope =
            "https://outlook.office365.com/IMAP.AccessAsUser.All offline_access openid profile";

        private static string Authority(string tenant)
        {
            string t = string.IsNullOrEmpty(tenant) ? "common" : tenant.Trim();
            return "https://login.microsoftonline.com/" + Uri.EscapeDataString(t).Replace("%2F", "/");
        }

        private static void EnableModernTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)12288;   // Tls12 | Tls13
            }
            catch
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }
            }
        }

        /// <summary>Requests a device code. Throws on a transport/configuration error.</summary>
        public static DeviceCodeInfo RequestDeviceCode(string tenant, string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException(
                    "No Microsoft 365 Client ID is configured. Set it in Tools -> OAuth Settings.");

            EnableModernTls();

            string url = Authority(tenant) + "/oauth2/v2.0/devicecode";
            string body = "client_id=" + Uri.EscapeDataString(clientId) +
                          "&scope=" + Uri.EscapeDataString(ImapScope);

            Dictionary<string, object> json = PostForm(url, body);
            if (json == null)
                throw new Exception("The device-code endpoint returned no response.");

            if (json.ContainsKey("error"))
                throw new Exception(Describe(json));

            DeviceCodeInfo info = new DeviceCodeInfo();
            info.UserCode = Str(json, "user_code");
            info.DeviceCode = Str(json, "device_code");
            info.VerificationUri = Str(json, "verification_uri");
            info.Message = Str(json, "message");
            info.ExpiresInSeconds = Int(json, "expires_in", 900);
            info.IntervalSeconds = Int(json, "interval", 5);
            return info;
        }

        /// <summary>
        /// Polls the token endpoint until the user completes sign-in, the code expires,
        /// or <paramref name="cancelled"/> returns true. Blocks; call on a background thread.
        /// </summary>
        public static TokenResult PollForToken(string tenant, string clientId, DeviceCodeInfo info,
                                               Func<bool> cancelled)
        {
            TokenResult result = new TokenResult();
            EnableModernTls();

            string url = Authority(tenant) + "/oauth2/v2.0/token";
            int interval = Math.Max(2, info.IntervalSeconds);
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, info.ExpiresInSeconds));

            while (DateTime.UtcNow < deadline)
            {
                if (cancelled != null && cancelled())
                {
                    result.Cancelled = true;
                    result.Error = "Sign-in cancelled.";
                    return result;
                }

                for (int i = 0; i < interval * 10; i++)
                {
                    Thread.Sleep(100);
                    if (cancelled != null && cancelled())
                    {
                        result.Cancelled = true;
                        result.Error = "Sign-in cancelled.";
                        return result;
                    }
                }

                string body = "grant_type=urn:ietf:params:oauth:grant-type:device_code" +
                              "&client_id=" + Uri.EscapeDataString(clientId) +
                              "&device_code=" + Uri.EscapeDataString(info.DeviceCode);

                Dictionary<string, object> json = PostForm(url, body);
                if (json == null)
                    continue;

                if (json.ContainsKey("error"))
                {
                    string error = Str(json, "error");
                    if (error == "authorization_pending")
                        continue;
                    if (error == "slow_down")
                    {
                        interval += 5;
                        continue;
                    }

                    result.Success = false;
                    result.Error = Describe(json);
                    return result;
                }

                result.Success = true;
                result.AccessToken = Str(json, "access_token");
                result.RefreshToken = Str(json, "refresh_token");
                result.ExpiresUtc = DateTime.UtcNow.AddSeconds(Int(json, "expires_in", 3600));
                result.Account = ExtractAccount(result.AccessToken, Str(json, "id_token"));
                return result;
            }

            result.Success = false;
            result.Error = "The sign-in request expired before it was completed.";
            return result;
        }

        /// <summary>Exchanges a refresh token for a fresh access token (silent re-auth).</summary>
        public static TokenResult RefreshToken(string tenant, string clientId, string refreshToken)
        {
            TokenResult result = new TokenResult();
            EnableModernTls();

            string url = Authority(tenant) + "/oauth2/v2.0/token";
            string body = "grant_type=refresh_token" +
                          "&client_id=" + Uri.EscapeDataString(clientId) +
                          "&scope=" + Uri.EscapeDataString(ImapScope) +
                          "&refresh_token=" + Uri.EscapeDataString(refreshToken ?? string.Empty);

            Dictionary<string, object> json = PostForm(url, body);
            if (json == null)
            {
                result.Error = "No response from the token endpoint.";
                return result;
            }
            if (json.ContainsKey("error"))
            {
                result.Error = Describe(json);
                return result;
            }

            result.Success = true;
            result.AccessToken = Str(json, "access_token");
            result.RefreshToken = Str(json, "refresh_token");
            if (string.IsNullOrEmpty(result.RefreshToken))
                result.RefreshToken = refreshToken;
            result.ExpiresUtc = DateTime.UtcNow.AddSeconds(Int(json, "expires_in", 3600));
            result.Account = ExtractAccount(result.AccessToken, Str(json, "id_token"));
            return result;
        }

        // --- HTTP + JSON helpers --------------------------------------------------

        private static Dictionary<string, object> PostForm(string url, string body)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.Accept = "application/json";
            req.Timeout = 30000;
            req.UserAgent = "MailTransferUtility95/1.0";

            byte[] data = Encoding.UTF8.GetBytes(body);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream())
                s.Write(data, 0, data.Length);

            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    return ReadJson(resp);
            }
            catch (WebException wex)
            {
                HttpWebResponse resp = wex.Response as HttpWebResponse;
                if (resp != null)
                {
                    using (resp)
                        return ReadJson(resp);
                }
                throw new Exception("Network error contacting Microsoft identity platform: " + wex.Message);
            }
        }

        private static Dictionary<string, object> ReadJson(HttpWebResponse resp)
        {
            using (Stream s = resp.GetResponseStream())
            {
                if (s == null) return null;
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                {
                    string text = r.ReadToEnd();
                    if (string.IsNullOrEmpty(text)) return null;
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    js.MaxJsonLength = 8 * 1024 * 1024;
                    return js.Deserialize<Dictionary<string, object>>(text);
                }
            }
        }

        private static string Describe(Dictionary<string, object> json)
        {
            string error = Str(json, "error");
            string desc = Str(json, "error_description");
            if (!string.IsNullOrEmpty(desc))
            {
                int nl = desc.IndexOf('\n');
                if (nl > 0) desc = desc.Substring(0, nl).Trim();
                if (nl < 0) { int r = desc.IndexOf('\r'); if (r > 0) desc = desc.Substring(0, r).Trim(); }
            }
            if (string.IsNullOrEmpty(error)) error = "unknown_error";
            return string.IsNullOrEmpty(desc) ? error : error + ": " + desc;
        }

        private static string ExtractAccount(string accessToken, string idToken)
        {
            string acct = ClaimFromJwt(idToken, "preferred_username");
            if (!string.IsNullOrEmpty(acct)) return acct;

            foreach (string claim in new[] { "preferred_username", "upn", "unique_name", "email", "smtp" })
            {
                acct = ClaimFromJwt(accessToken, claim);
                if (!string.IsNullOrEmpty(acct)) return acct;
            }
            return "(signed in)";
        }

        private static string ClaimFromJwt(string jwt, string claim)
        {
            try
            {
                if (string.IsNullOrEmpty(jwt)) return null;
                string[] parts = jwt.Split('.');
                if (parts.Length < 2) return null;

                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                byte[] bytes = Convert.FromBase64String(payload);
                string jsonText = Encoding.UTF8.GetString(bytes);
                Dictionary<string, object> json =
                    new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(jsonText);
                return Str(json, claim);
            }
            catch
            {
                return null;
            }
        }

        private static string Str(Dictionary<string, object> json, string key)
        {
            object v;
            if (json != null && json.TryGetValue(key, out v) && v != null)
                return v.ToString();
            return null;
        }

        private static int Int(Dictionary<string, object> json, string key, int fallback)
        {
            object v;
            if (json != null && json.TryGetValue(key, out v) && v != null)
            {
                int n;
                if (int.TryParse(v.ToString(), out n))
                    return n;
            }
            return fallback;
        }
    }
}
