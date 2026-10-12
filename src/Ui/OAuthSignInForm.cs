using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Microsoft 365 sign-in using the OAuth 2.0 device-code flow. The administrator
    /// completes sign-in on Microsoft's own web page, so this application never sees or
    /// handles the Microsoft password. The access token is kept only in memory.
    /// </summary>
    public sealed class OAuthSignInForm : Win95Form
    {
        private readonly string _tenant;
        private readonly string _clientId;
        private volatile bool _cancelled;
        private DeviceCodeInfo _device;

        private ClassicLabel _instructions;
        private TextBox _urlBox;
        private ClassicLabel _codeLabel;
        private ClassicLabel _status;
        private ClassicButton _openButton;
        private ClassicButton _cancelButton;

        public TokenResult Result { get; private set; }

        public OAuthSignInForm(string tenant, string clientId)
        {
            _tenant = tenant;
            _clientId = clientId;

            BuildChrome("Sign in with Microsoft");
            CloseOnEscape = true;
            Width = 470;
            Height = 290;
            StartPosition = FormStartPosition.CenterParent;

            Build();
        }

        private void Build()
        {
            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Win95Style.Face;
            body.Paint += delegate (object s, PaintEventArgs e)
            {
                Win95Style.PrepareGraphics(e.Graphics);
                Win95MessageBox.DrawIcon(e.Graphics, 16, 16, Win95Icon.Information);
            };
            ClientSurface.Controls.Add(body);

            _instructions = new ClassicLabel();
            _instructions.WordWrap = true;
            _instructions.SetBounds(60, 16, 392, 52);
            _instructions.Text = "Preparing Microsoft sign-in...";
            body.Controls.Add(_instructions);

            ClassicLabel urlCaption = new ClassicLabel();
            urlCaption.Text = "Sign-in page:";
            urlCaption.SetBounds(60, 74, 90, 16);
            body.Controls.Add(urlCaption);

            SunkenPanel urlHost = new SunkenPanel();
            urlHost.SetBounds(150, 72, 290, 21);
            body.Controls.Add(urlHost);
            _urlBox = new TextBox();
            _urlBox.BorderStyle = BorderStyle.None;
            _urlBox.ReadOnly = true;
            _urlBox.Font = Win95Style.CreateFontSafe();
            _urlBox.BackColor = Win95Style.FieldBack;
            _urlBox.SetBounds(4, 3, 282, 15);
            urlHost.Controls.Add(_urlBox);

            ClassicLabel codeCaption = new ClassicLabel();
            codeCaption.Text = "Your code:";
            codeCaption.SetBounds(60, 104, 90, 16);
            body.Controls.Add(codeCaption);

            _codeLabel = new ClassicLabel();
            _codeLabel.Font = new Font(Win95Style.CreateBoldFont().FontFamily, 14f, FontStyle.Bold);
            _codeLabel.SetBounds(150, 100, 290, 26);
            _codeLabel.Text = "--------";
            body.Controls.Add(_codeLabel);

            _openButton = new ClassicButton();
            _openButton.Text = "Open sign-in page";
            _openButton.SetBounds(150, 134, 150, 23);
            _openButton.Enabled = false;
            _openButton.Click += delegate { OpenBrowser(); };
            body.Controls.Add(_openButton);

            _status = new ClassicLabel();
            _status.WordWrap = true;
            _status.SetBounds(60, 168, 392, 34);
            _status.TextColorOverride = Win95Style.FaceDark;
            _status.Text = string.Empty;
            body.Controls.Add(_status);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 38;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            _cancelButton = new ClassicButton();
            _cancelButton.Text = "Cancel";
            _cancelButton.SetBounds(Width - 110, 7, 90, 23);
            _cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cancelButton.IsCancelButton = true;
            _cancelButton.Click += delegate { _cancelled = true; Close(); };
            bar.Controls.Add(_cancelButton);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Thread t = new Thread(StartDeviceFlow) { IsBackground = true };
            t.Start();
        }

        private void StartDeviceFlow()
        {
            try
            {
                DeviceCodeInfo info = OAuthService.RequestDeviceCode(_tenant, _clientId);
                _device = info;

                Ui(delegate
                {
                    _instructions.Text = "To sign in, open the sign-in page below and enter this one-time code. " +
                                         "Sign in with the Microsoft 365 account to migrate.";
                    _urlBox.Text = info.VerificationUri;
                    _codeLabel.Text = info.UserCode;
                    _openButton.Enabled = true;
                    _status.Text = "Waiting for you to complete sign-in in the browser...";
                });

                TokenResult r = OAuthService.PollForToken(_tenant, _clientId, info, delegate { return _cancelled; });

                Ui(delegate
                {
                    if (r.Cancelled)
                        return;

                    if (r.Success)
                    {
                        Result = r;
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else
                    {
                        _status.TextColorOverride = Win95Style.StatusFail;
                        _status.Text = "Sign-in failed: " + (r.Error ?? "unknown error");
                        _cancelButton.Text = "Close";
                    }
                });
            }
            catch (Exception ex)
            {
                Ui(delegate
                {
                    _status.TextColorOverride = Win95Style.StatusFail;
                    _status.Text = "Sign-in could not start: " + ex.Message;
                    _cancelButton.Text = "Close";
                });
            }
        }

        private void OpenBrowser()
        {
            try
            {
                if (_device != null && !string.IsNullOrEmpty(_device.VerificationUri))
                    Process.Start(_device.VerificationUri);
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowWarning(this, "Open sign-in page",
                    "Could not open the browser automatically.",
                    "Please open this address manually:\r\n" +
                    (_device != null ? _device.VerificationUri : "") + "\r\n\r\n" + ex.Message);
            }
        }

        private void Ui(MethodInvoker action)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch
            {
                // Form was closed mid-flight; nothing to do.
            }
        }
    }
}
