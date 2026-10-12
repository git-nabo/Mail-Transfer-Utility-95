using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// A reusable connection panel for one mailbox endpoint. It shows either normal IMAP
    /// fields (host/port/encryption/username/password) or the Microsoft 365 OAuth fields
    /// (account + "Sign in with Microsoft"), depending on the chosen auth mode. The same
    /// panel is used for both the source and the destination, and it is reconfigured when
    /// the transfer direction changes.
    /// </summary>
    public sealed class EndpointPanel : Panel
    {
        private readonly EndpointRole _role;
        private AuthMode _mode = AuthMode.Password;

        // IMAP controls
        private ComboBox _preset;
        private TextBox _server;
        private TextBox _port;
        private ComboBox _encryption;
        private TextBox _username;
        private TextBox _password;
        private ClassicLabel _presetHint;

        // OAuth controls
        private TextBox _oauthServer;
        private TextBox _oauthAccount;
        private ClassicLabel _authLabel;
        private ClassicLabel _statusLabel;
        private ClassicButton _signInButton;

        // shared
        private ClassicButton _testButton;

        // OAuth runtime state (never persisted)
        private string _accessToken;
        private string _refreshToken;
        private DateTime _tokenExpiresUtc;
        private string _account;

        public event EventHandler AuthenticationChanged;

        public EndpointPanel(EndpointRole role)
        {
            _role = role;
            BackColor = Win95Style.Face;
            BuildControls();
            SetMode(AuthMode.Password);
        }

        public EndpointRole Role { get { return _role; } }
        public AuthMode Mode { get { return _mode; } }
        public bool IsAuthenticated { get { return _mode != AuthMode.OAuth2 || !string.IsNullOrEmpty(_accessToken); } }

        // --- Layout ---------------------------------------------------------------

        private const int LabelX = 6;
        private const int LabelW = 78;
        private const int FieldX = 88;

        private ClassicLabel AddLabel(string text, int y, int width)
        {
            ClassicLabel l = new ClassicLabel();
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.SetBounds(LabelX, y, width, 18);
            Controls.Add(l);
            return l;
        }

        private TextBox AddField(int y, int width, bool password)
        {
            SunkenPanel host = new SunkenPanel();
            host.SetBounds(FieldX, y - 2, width, 21);
            Controls.Add(host);

            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.None;
            t.Font = Win95Style.CreateFontSafe();
            t.BackColor = Win95Style.FieldBack;
            if (password) t.UseSystemPasswordChar = true;
            t.SetBounds(4, 3, width - 8, 15);
            host.Controls.Add(t);
            host.Tag = t;
            return t;
        }

        private ComboBox AddCombo(int y, int width)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Win95Style.CreateFontSafe();
            c.BackColor = Win95Style.FieldBack;
            c.SetBounds(FieldX, y - 2, width, 21);
            Controls.Add(c);
            return c;
        }

        private void BuildControls()
        {
            int fullW = 268;

            // --- IMAP mode controls ---
            AddLabel("Preset:", 6, LabelW).Name = "lblPreset";
            _preset = AddCombo(6, fullW);
            foreach (ServerPreset p in ServerPreset.All())
                _preset.Items.Add(p);
            _preset.SelectedIndexChanged += delegate { ApplyPreset(); };

            AddLabel("Server:", 34, LabelW).Name = "lblServer";
            _server = AddField(34, 190, false);

            ClassicLabel portLbl = new ClassicLabel();
            portLbl.Text = "Port:";
            portLbl.TextAlign = ContentAlignment.MiddleLeft;
            portLbl.SetBounds(FieldX + 196, 34, 30, 18);
            Controls.Add(portLbl);
            _port = AddFieldAt(FieldX + 228, 34, 40, false);

            AddLabel("Encryption:", 62, LabelW).Name = "lblEnc";
            _encryption = AddCombo(62, 140);
            _encryption.Items.Add("SSL/TLS");
            _encryption.Items.Add("STARTTLS");
            _encryption.Items.Add("None");
            _encryption.SelectedIndex = 0;

            AddLabel("Username:", 90, LabelW).Name = "lblUser";
            _username = AddField(90, fullW, false);

            AddLabel("Password:", 118, LabelW).Name = "lblPass";
            _password = AddField(118, fullW, true);

            _presetHint = new ClassicLabel();
            _presetHint.TextColorOverride = Win95Style.FaceDark;
            _presetHint.SetBounds(LabelX, 144, 360, 16);
            Controls.Add(_presetHint);

            // --- OAuth mode controls ---
            AddLabelNamed("Server:", 34, "lblOAuthServer");
            _oauthServer = AddField(34, fullW, false);
            _oauthServer.Text = "outlook.office365.com";
            _oauthServer.ReadOnly = true;
            _oauthServer.BackColor = Win95Style.Face;

            AddLabelNamed("Account:", 62, "lblOAuthAccount");
            _oauthAccount = AddField(62, fullW, false);

            _authLabel = AddLabelNamed("Auth:", 90, "lblOAuthAuth");
            ClassicLabel authVal = new ClassicLabel();
            authVal.Name = "authVal";
            authVal.Text = "Microsoft OAuth 2.0 / Modern Authentication";
            authVal.SetBounds(FieldX, 90, 280, 18);
            authVal.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(authVal);

            AddLabelNamed("Status:", 112, "lblOAuthStatus");
            _statusLabel = new ClassicLabel();
            _statusLabel.SetBounds(FieldX, 112, 280, 18);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_statusLabel);

            _signInButton = new ClassicButton();
            _signInButton.Text = "Sign in with Microsoft";
            _signInButton.SetBounds(FieldX, 136, 160, 23);
            _signInButton.Click += delegate { SignIn(); };
            Controls.Add(_signInButton);

            // --- shared Test button ---
            _testButton = new ClassicButton();
            _testButton.Text = "Test Connection";
            _testButton.SetBounds(LabelX, 168, 130, 23);
            _testButton.Click += delegate { RunTest(); };
            Controls.Add(_testButton);

            UpdateOAuthStatus();
        }

        private ClassicLabel AddLabelNamed(string text, int y, string name)
        {
            ClassicLabel l = new ClassicLabel();
            l.Text = text;
            l.Name = name;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.SetBounds(LabelX, y, LabelW, 18);
            Controls.Add(l);
            return l;
        }

        private TextBox AddFieldAt(int x, int y, int width, bool password)
        {
            SunkenPanel host = new SunkenPanel();
            host.SetBounds(x, y - 2, width, 21);
            Controls.Add(host);

            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.None;
            t.Font = Win95Style.CreateFontSafe();
            t.BackColor = Win95Style.FieldBack;
            if (password) t.UseSystemPasswordChar = true;
            t.SetBounds(4, 3, width - 8, 15);
            host.Controls.Add(t);
            return t;
        }

        // --- Mode switching -------------------------------------------------------

        public void SetMode(AuthMode mode)
        {
            _mode = mode;
            bool imap = mode != AuthMode.OAuth2;

            // IMAP controls
            SetVisible(imap, "lblPreset", "lblServer", "lblEnc", "lblUser", "lblPass");
            _preset.Visible = imap;
            _server.Parent.Visible = imap;
            _port.Parent.Visible = imap;
            _encryption.Visible = imap;
            _username.Parent.Visible = imap;
            _password.Parent.Visible = imap;
            _presetHint.Visible = imap;
            foreach (Control c in Controls)
                if (c is ClassicLabel && c.Text == "Port:") c.Visible = imap;

            // OAuth controls
            SetVisible(!imap, "lblOAuthServer", "lblOAuthAccount", "lblOAuthAuth", "lblOAuthStatus", "authVal");
            _oauthServer.Parent.Visible = !imap;
            _oauthAccount.Parent.Visible = !imap;
            _statusLabel.Visible = !imap;
            _signInButton.Visible = !imap;

            if (!imap)
            {
                // Microsoft 365 preset defaults (host fixed to Exchange Online).
                if (string.IsNullOrEmpty(_oauthServer.Text)) _oauthServer.Text = "outlook.office365.com";
            }
            else if (_preset.SelectedIndex < 0)
            {
                _preset.SelectedIndex = 0;
            }

            UpdateOAuthStatus();
        }

        private void SetVisible(bool visible, params string[] names)
        {
            foreach (string n in names)
            {
                Control[] found = Controls.Find(n, false);
                foreach (Control c in found) c.Visible = visible;
            }
        }

        private void ApplyPreset()
        {
            ServerPreset p = _preset.SelectedItem as ServerPreset;
            if (p == null) return;

            if (!string.IsNullOrEmpty(p.Host)) _server.Text = p.Host;
            _port.Text = p.Port.ToString();
            _encryption.SelectedIndex = (int)p.Encryption;
            _presetHint.Text = p.Hint ?? string.Empty;

            // A preset that requires OAuth (Microsoft 365) cannot be used as a plain IMAP
            // endpoint - the direction radio controls OAuth, so just show the hint.
        }

        // --- OAuth ----------------------------------------------------------------

        private void UpdateOAuthStatus()
        {
            if (_statusLabel == null) return;

            if (!string.IsNullOrEmpty(_accessToken))
            {
                _statusLabel.Text = "Authenticated" + (string.IsNullOrEmpty(_account) ? "" : "  -  " + _account);
                _statusLabel.TextColorOverride = Win95Style.StatusOk;
                _signInButton.Text = "Sign in again";
            }
            else
            {
                _statusLabel.Text = "Not authenticated";
                _statusLabel.TextColorOverride = Win95Style.StatusFail;
                _signInButton.Text = "Sign in with Microsoft";
            }
            _statusLabel.Invalidate();
        }

        private void SignIn()
        {
            Settings.Load();
            if (string.IsNullOrEmpty(Settings.OAuthClientId))
            {
                Win95MessageBox.ShowWarning(FindForm(), "Microsoft 365 Sign-in",
                    "No Microsoft 365 application (client) ID is configured yet.",
                    "Open Tools -> OAuth Settings and enter your Entra ID tenant and client ID " +
                    "before signing in. The application never stores your Microsoft password.");
                return;
            }

            using (OAuthSignInForm dlg = new OAuthSignInForm(Settings.OAuthTenantId, Settings.OAuthClientId))
            {
                dlg.ShowDialog(FindForm());
                if (dlg.Result != null && dlg.Result.Success)
                {
                    _accessToken = dlg.Result.AccessToken;
                    _refreshToken = dlg.Result.RefreshToken;
                    _tokenExpiresUtc = dlg.Result.ExpiresUtc;
                    _account = dlg.Result.Account;
                    if (!string.IsNullOrEmpty(_account) && string.IsNullOrEmpty(_oauthAccount.Text))
                        _oauthAccount.Text = _account;
                    UpdateOAuthStatus();
                    EventHandler h = AuthenticationChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Ensures a non-expired access token, refreshing silently if possible.</summary>
        public bool EnsureFreshToken()
        {
            if (_mode != AuthMode.OAuth2)
                return true;
            if (string.IsNullOrEmpty(_accessToken))
                return false;

            if (DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
                return true;

            if (!string.IsNullOrEmpty(_refreshToken))
            {
                TokenResult r = OAuthService.RefreshToken(Settings.OAuthTenantId, Settings.OAuthClientId, _refreshToken);
                if (r.Success)
                {
                    _accessToken = r.AccessToken;
                    _refreshToken = r.RefreshToken;
                    _tokenExpiresUtc = r.ExpiresUtc;
                    if (!string.IsNullOrEmpty(r.Account)) _account = r.Account;
                    UpdateOAuthStatus();
                    return true;
                }
            }
            return !string.IsNullOrEmpty(_accessToken);
        }

        // --- Endpoint get/set -----------------------------------------------------

        public TransferEndpoint GetEndpoint()
        {
            TransferEndpoint ep = new TransferEndpoint();
            ep.Auth = _mode;

            if (_mode == AuthMode.OAuth2)
            {
                ep.Host = string.IsNullOrEmpty(_oauthServer.Text) ? "outlook.office365.com" : _oauthServer.Text.Trim();
                ep.Port = 993;
                ep.Encryption = EncryptionMode.SslTls;
                ep.Username = _oauthAccount.Text.Trim();
                ep.Account = string.IsNullOrEmpty(_account) ? _oauthAccount.Text.Trim() : _account;
                ep.RuntimeAccessToken = _accessToken;
                ep.RuntimeRefreshToken = _refreshToken;
            }
            else
            {
                ep.Host = _server.Text.Trim();
                int port;
                ep.Port = int.TryParse(_port.Text.Trim(), out port) ? port : 993;
                ep.Encryption = (EncryptionMode)Math.Max(0, _encryption.SelectedIndex);
                ep.Username = _username.Text.Trim();
                ep.RuntimePassword = _password.Text;
            }
            return ep;
        }

        public void LoadEndpoint(TransferEndpoint ep)
        {
            if (ep == null) return;

            if (ep.Auth == AuthMode.OAuth2)
            {
                SetMode(AuthMode.OAuth2);
                if (!string.IsNullOrEmpty(ep.Host)) _oauthServer.Text = ep.Host;
                _oauthAccount.Text = ep.Account ?? ep.Username ?? string.Empty;
            }
            else
            {
                SetMode(AuthMode.Password);
                _server.Text = ep.Host ?? string.Empty;
                _port.Text = ep.Port.ToString();
                _encryption.SelectedIndex = Math.Min(2, Math.Max(0, (int)ep.Encryption));
                _username.Text = ep.Username ?? string.Empty;
                _password.Text = ep.RuntimePassword ?? string.Empty;
            }
        }

        /// <summary>Validation message, or null when the endpoint is ready to use.</summary>
        public string Validate()
        {
            if (_mode == AuthMode.OAuth2)
            {
                if (string.IsNullOrEmpty(_accessToken))
                    return (_role == EndpointRole.Source ? "Source" : "Destination") +
                           ": sign in with Microsoft before continuing.";
                if (string.IsNullOrEmpty(_oauthAccount.Text.Trim()))
                    return (_role == EndpointRole.Source ? "Source" : "Destination") +
                           ": enter the Microsoft 365 account (user@domain).";
                return null;
            }

            if (string.IsNullOrEmpty(_server.Text.Trim()))
                return (_role == EndpointRole.Source ? "Source" : "Destination") + ": enter a server host name.";
            if (string.IsNullOrEmpty(_username.Text.Trim()))
                return (_role == EndpointRole.Source ? "Source" : "Destination") + ": enter a user name.";
            int port;
            if (!int.TryParse(_port.Text.Trim(), out port) || port <= 0 || port > 65535)
                return (_role == EndpointRole.Source ? "Source" : "Destination") + ": enter a valid port (1-65535).";
            if (string.IsNullOrEmpty(_password.Text))
                return (_role == EndpointRole.Source ? "Source" : "Destination") + ": enter a password.";
            return null;
        }

        /// <summary>Public entry point used by the Transfer menu's Test Source/Destination.</summary>
        public void TriggerTest()
        {
            RunTest();
        }

        private void RunTest()
        {
            if (_mode == AuthMode.OAuth2 && string.IsNullOrEmpty(_accessToken))
            {
                Win95MessageBox.ShowWarning(FindForm(), "Test Connection",
                    "Sign in with Microsoft first, then test the connection.");
                return;
            }

            if (_mode != AuthMode.OAuth2)
            {
                string v = Validate();
                if (v != null)
                {
                    Win95MessageBox.ShowWarning(FindForm(), "Test Connection", v);
                    return;
                }
            }

            EnsureFreshToken();
            TransferEndpoint ep = GetEndpoint();
            using (ConnectionTestForm dlg = new ConnectionTestForm(ep, Settings.TimeoutSeconds))
                dlg.ShowDialog(FindForm());
        }
    }
}
