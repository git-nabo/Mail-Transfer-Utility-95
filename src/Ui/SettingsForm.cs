using System;
using System.Drawing;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Settings: the imapsync executable, general run options and the Microsoft 365
    /// OAuth configuration. Nothing developer-specific is hardcoded and no secret is ever
    /// written - only a tenant ID and an application (client) ID.
    /// </summary>
    public sealed class SettingsForm : Win95Form
    {
        private TextBox _imapsyncPath;
        private ClassicLabel _imapsyncStatus;
        private TextBox _timeout;
        private TextBox _workDir;
        private ClassicCheckBox _logging;
        private TextBox _tenant;
        private TextBox _clientId;

        private readonly int _initialTab;

        public SettingsForm(int initialTab)
        {
            _initialTab = initialTab;
            Settings.Load();

            BuildChrome("Settings");
            CloseOnEscape = true;
            Width = 520;
            Height = 372;
            StartPosition = FormStartPosition.CenterParent;

            Build();
        }

        private void Build()
        {
            TablessTabControl tabs = new TablessTabControl();
            tabs.SetBounds(10, 8, Width - 24, Height - 90);
            tabs.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            ClientSurface.Controls.Add(tabs);

            tabs.AddPage(new TablessPage("imapsync", BuildImapsyncPage()));
            tabs.AddPage(new TablessPage("General", BuildGeneralPage()));
            tabs.AddPage(new TablessPage("Microsoft 365", BuildOAuthPage()));
            tabs.EnsureFirstPageVisible();
            if (_initialTab > 0) tabs.Activate(_initialTab);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 42;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            ClassicButton ok = new ClassicButton();
            ok.Text = "OK";
            ok.SetBounds(Width - 196, 9, 86, 23);
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ok.IsDefaultButton = true;
            ok.Click += delegate { SaveAndClose(); };
            bar.Controls.Add(ok);

            ClassicButton cancel = new ClassicButton();
            cancel.Text = "Cancel";
            cancel.SetBounds(Width - 104, 9, 86, 23);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.IsCancelButton = true;
            cancel.Click += delegate { Close(); };
            bar.Controls.Add(cancel);

            RefreshImapsyncStatus();
        }

        private Control BuildImapsyncPage()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Win95Style.Face };

            ClassicLabel intro = new ClassicLabel();
            intro.WordWrap = true;
            intro.SetBounds(12, 10, 476, 44);
            intro.Text = "imapsync is a separate third-party tool (imapsync.lamiral.info) and is not " +
                         "bundled. If it is not found automatically, set its path here.";
            p.Controls.Add(intro);

            _imapsyncStatus = new ClassicLabel();
            _imapsyncStatus.SetBounds(12, 60, 470, 16);
            p.Controls.Add(_imapsyncStatus);

            ClassicLabel lbl = new ClassicLabel();
            lbl.Text = "imapsync.exe path:";
            lbl.SetBounds(12, 92, 120, 16);
            p.Controls.Add(lbl);

            SunkenPanel host = new SunkenPanel();
            host.SetBounds(12, 110, 360, 21);
            p.Controls.Add(host);
            _imapsyncPath = new TextBox();
            _imapsyncPath.BorderStyle = BorderStyle.None;
            _imapsyncPath.Font = Win95Style.CreateFontSafe();
            _imapsyncPath.BackColor = Win95Style.FieldBack;
            _imapsyncPath.SetBounds(4, 3, 352, 15);
            _imapsyncPath.Text = Settings.ImapSyncPath ?? string.Empty;
            host.Controls.Add(_imapsyncPath);

            ClassicButton browse = new ClassicButton();
            browse.Text = "Browse...";
            browse.SetBounds(380, 109, 90, 23);
            browse.Click += delegate { BrowseImapsync(); };
            p.Controls.Add(browse);

            ClassicButton recheck = new ClassicButton();
            recheck.Text = "Re-check";
            recheck.SetBounds(12, 140, 90, 23);
            recheck.Click += delegate { Settings.ImapSyncPath = _imapsyncPath.Text.Trim(); RefreshImapsyncStatus(); };
            p.Controls.Add(recheck);

            ClassicLabel order = new ClassicLabel();
            order.WordWrap = true;
            order.SetBounds(12, 172, 476, 48);
            order.Text = "Search order: 1) tools\\imapsync next to the EXE   2) the application folder   " +
                         "3) this configured path   4) the PATH environment variable.";
            p.Controls.Add(order);

            return p;
        }

        private Control BuildGeneralPage()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Win95Style.Face };

            ClassicLabel t = new ClassicLabel();
            t.Text = "Default timeout (seconds):";
            t.SetBounds(12, 16, 160, 16);
            p.Controls.Add(t);

            SunkenPanel th = new SunkenPanel();
            th.SetBounds(176, 14, 60, 21);
            p.Controls.Add(th);
            _timeout = new TextBox();
            _timeout.BorderStyle = BorderStyle.None;
            _timeout.Font = Win95Style.CreateFontSafe();
            _timeout.BackColor = Win95Style.FieldBack;
            _timeout.SetBounds(4, 3, 52, 15);
            _timeout.Text = Settings.TimeoutSeconds.ToString();
            th.Controls.Add(_timeout);

            ClassicLabel w = new ClassicLabel();
            w.Text = "Working directory:";
            w.SetBounds(12, 52, 160, 16);
            p.Controls.Add(w);

            SunkenPanel wh = new SunkenPanel();
            wh.SetBounds(12, 70, 360, 21);
            p.Controls.Add(wh);
            _workDir = new TextBox();
            _workDir.BorderStyle = BorderStyle.None;
            _workDir.Font = Win95Style.CreateFontSafe();
            _workDir.BackColor = Win95Style.FieldBack;
            _workDir.SetBounds(4, 3, 352, 15);
            _workDir.Text = Settings.WorkingDirectoryOverride ?? string.Empty;
            wh.Controls.Add(_workDir);

            ClassicButton browse = new ClassicButton();
            browse.Text = "Browse...";
            browse.SetBounds(380, 69, 90, 23);
            browse.Click += delegate { BrowseWorkDir(); };
            p.Controls.Add(browse);

            ClassicLabel wh2 = new ClassicLabel();
            wh2.WordWrap = true;
            wh2.SetBounds(12, 94, 476, 16);
            wh2.TextColorOverride = Win95Style.FaceDark;
            wh2.Text = "Leave empty to use the default under %LocalAppData%\\MailTransfer95\\work.";
            p.Controls.Add(wh2);

            _logging = new ClassicCheckBox();
            _logging.Text = "Enable migration logging (timestamps, addresses, counts, errors - never secrets)";
            _logging.SetBounds(12, 128, 470, 16);
            _logging.Checked = Settings.LoggingEnabled;
            p.Controls.Add(_logging);

            ClassicLabel ln = new ClassicLabel();
            ln.WordWrap = true;
            ln.SetBounds(12, 150, 476, 32);
            ln.TextColorOverride = Win95Style.FaceDark;
            ln.Text = "Logging is off by default. Passwords and OAuth tokens are never logged.";
            p.Controls.Add(ln);

            return p;
        }

        private Control BuildOAuthPage()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Win95Style.Face };

            ClassicLabel intro = new ClassicLabel();
            intro.WordWrap = true;
            intro.SetBounds(12, 10, 476, 66);
            intro.Text = "Microsoft 365 / Exchange Online requires OAuth 2.0 (Modern Authentication). " +
                         "Register a public client app in Entra ID with the delegated permission " +
                         "Office 365 Exchange Online > IMAP.AccessAsUser.All, allow public client flows, " +
                         "then enter its IDs below. No client secret is needed or stored.";
            p.Controls.Add(intro);

            ClassicLabel t = new ClassicLabel();
            t.Text = "Tenant ID:";
            t.SetBounds(12, 84, 90, 16);
            p.Controls.Add(t);

            SunkenPanel th = new SunkenPanel();
            th.SetBounds(104, 82, 360, 21);
            p.Controls.Add(th);
            _tenant = new TextBox();
            _tenant.BorderStyle = BorderStyle.None;
            _tenant.Font = Win95Style.CreateFontSafe();
            _tenant.BackColor = Win95Style.FieldBack;
            _tenant.SetBounds(4, 3, 352, 15);
            _tenant.Text = Settings.OAuthTenantId ?? "common";
            th.Controls.Add(_tenant);

            ClassicLabel th2 = new ClassicLabel();
            th2.WordWrap = true;
            th2.SetBounds(104, 104, 384, 16);
            th2.TextColorOverride = Win95Style.FaceDark;
            th2.Text = "Your directory (tenant) GUID, a verified domain, or common / organizations.";
            p.Controls.Add(th2);

            ClassicLabel c = new ClassicLabel();
            c.Text = "Client ID:";
            c.SetBounds(12, 132, 90, 16);
            p.Controls.Add(c);

            SunkenPanel ch = new SunkenPanel();
            ch.SetBounds(104, 130, 360, 21);
            p.Controls.Add(ch);
            _clientId = new TextBox();
            _clientId.BorderStyle = BorderStyle.None;
            _clientId.Font = Win95Style.CreateFontSafe();
            _clientId.BackColor = Win95Style.FieldBack;
            _clientId.SetBounds(4, 3, 352, 15);
            _clientId.Text = Settings.OAuthClientId ?? string.Empty;
            ch.Controls.Add(_clientId);

            ClassicLabel c2 = new ClassicLabel();
            c2.WordWrap = true;
            c2.SetBounds(104, 152, 384, 16);
            c2.TextColorOverride = Win95Style.FaceDark;
            c2.Text = "The Application (client) ID of your app registration.";
            p.Controls.Add(c2);

            return p;
        }

        private void RefreshImapsyncStatus()
        {
            ImapSyncLocation loc = ImapSyncLocator.Locate();
            if (loc.Found)
            {
                _imapsyncStatus.TextColorOverride = Win95Style.StatusOk;
                _imapsyncStatus.Text = "imapsync found  -  " + loc.Source + ": " + loc.Path;
            }
            else
            {
                _imapsyncStatus.TextColorOverride = Win95Style.StatusFail;
                _imapsyncStatus.Text = "imapsync not configured  -  set a path below or install imapsync.";
            }
            _imapsyncStatus.Invalidate();
        }

        private void BrowseImapsync()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Locate imapsync.exe";
                dlg.Filter = "imapsync (imapsync*.exe)|imapsync*.exe|Executables (*.exe)|*.exe|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _imapsyncPath.Text = dlg.FileName;
                    Settings.ImapSyncPath = dlg.FileName;
                    RefreshImapsyncStatus();
                }
            }
        }

        private void BrowseWorkDir()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select a working directory for run files and logs";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _workDir.Text = dlg.SelectedPath;
            }
        }

        private void SaveAndClose()
        {
            Settings.ImapSyncPath = _imapsyncPath.Text.Trim();

            int t;
            if (int.TryParse(_timeout.Text.Trim(), out t) && t > 0)
                Settings.TimeoutSeconds = Math.Min(3600, t);

            Settings.WorkingDirectoryOverride = _workDir.Text.Trim();
            Settings.LoggingEnabled = _logging.Checked;
            Settings.OAuthTenantId = string.IsNullOrEmpty(_tenant.Text.Trim()) ? "common" : _tenant.Text.Trim();
            Settings.OAuthClientId = _clientId.Text.Trim();

            Settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
