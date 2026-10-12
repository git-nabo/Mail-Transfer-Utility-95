using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// The main window: choose a transfer direction, configure the source and
    /// destination mailboxes, set options, and start the migration. It looks like
    /// Windows 95 but behaves like a modern desktop app - single-click controls,
    /// draggable caption, working minimise/maximise/close and Alt+F4.
    /// </summary>
    public sealed class MainForm : Win95Form
    {
        private MenuStrip _menu;
        private ToolStripMenuItem _dryRunMenuItem;
        private ToolStripMenuItem _loggingMenuItem;

        private ClassicRadioButton _rImapImap;
        private ClassicRadioButton _rImapM365;
        private ClassicRadioButton _rM365Imap;

        private ClassicGroupBox _sourceGroup;
        private ClassicGroupBox _destGroup;
        private EndpointPanel _source;
        private EndpointPanel _dest;

        private ClassicCheckBox _optCopyAll;
        private ClassicCheckBox _optDates;
        private ClassicCheckBox _optFlags;
        private ClassicCheckBox _optSkipDup;
        private ClassicCheckBox _optAutomap;
        private ClassicCheckBox _optSubscribe;
        private ClassicCheckBox _optDryRun;
        private ClassicCheckBox _optDeleteExtra;
        private ClassicButton _selectFoldersButton;
        private ClassicButton _folderMapButton;
        private ClassicButton _startButton;

        private Win95StatusBar _statusBar;

        private string _currentJobName;
        private List<string> _selectedFolders = new List<string>();
        private List<FolderMapping> _folderMappings = new List<FolderMapping>();

        public MainForm()
        {
            Settings.Load();
            Settings.EnsureWorkingDirectory();

            BuildChrome("Mail Transfer Utility 95");
            AllowMaximize = false;
            Width = 866;
            Height = 566;

            BuildMenu();
            BuildLayout();
            ApplyTransferType(TransferType.ImapToImap);
            UpdateStatus();
        }

        // --- Menu -----------------------------------------------------------------

        private void BuildMenu()
        {
            _menu = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add("&New Transfer", null, delegate { NewTransfer(); });
            file.DropDownItems.Add("&Open Job...", null, delegate { OpenJob(); });
            file.DropDownItems.Add("&Save Job...", null, delegate { SaveJob(false); });
            file.DropDownItems.Add("Save Job &As...", null, delegate { SaveJob(true); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("&Options...", null, delegate { ShowSettings(1); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("E&xit", null, delegate { Close(); });

            ToolStripMenuItem transfer = new ToolStripMenuItem("&Transfer");
            transfer.DropDownItems.Add("Test &Source", null, delegate { _source.TriggerTest(); });
            transfer.DropDownItems.Add("Test &Destination", null, delegate { _dest.TriggerTest(); });
            transfer.DropDownItems.Add(new ToolStripSeparator());
            transfer.DropDownItems.Add("&Start Transfer", null, delegate { StartTransfer(); });
            transfer.DropDownItems.Add("&Batch Transfer (multiple accounts)...", null, delegate { OpenBatch(); });
            _dryRunMenuItem = new ToolStripMenuItem("Dry &Run (simulation)");
            _dryRunMenuItem.Click += delegate { _optDryRun.Checked = !_optDryRun.Checked; };
            transfer.DropDownItems.Add(_dryRunMenuItem);

            ToolStripMenuItem tools = new ToolStripMenuItem("T&ools");
            tools.DropDownItems.Add("Configure &imapsync...", null, delegate { ShowSettings(0); });
            tools.DropDownItems.Add("&OAuth Settings...", null, delegate { ShowSettings(2); });
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add("View &Logs...", null, delegate { ViewLogs(); });
            _loggingMenuItem = new ToolStripMenuItem("Enable &Logging");
            _loggingMenuItem.Checked = Settings.LoggingEnabled;
            _loggingMenuItem.Click += delegate { ToggleLogging(); };
            tools.DropDownItems.Add(_loggingMenuItem);
            tools.DropDownItems.Add("Open &Working Folder", null, delegate { OpenWorkingFolder(); });

            ToolStripMenuItem help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add("&Documentation", null, delegate { ShowDocumentation(); });
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add("&About Mail Transfer Utility 95", null, delegate { ShowAbout(); });

            _menu.Items.AddRange(new ToolStripItem[] { file, transfer, tools, help });
        }

        // --- Layout ---------------------------------------------------------------

        private void BuildLayout()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(root);

            // Transfer type.
            ClassicGroupBox typeGroup = new ClassicGroupBox();
            typeGroup.Text = "Transfer Type";
            typeGroup.SetBounds(10, 8, 838, 52);
            root.Controls.Add(typeGroup);

            _rImapImap = MakeRadio("IMAP  ->  IMAP", 18);
            _rImapM365 = MakeRadio("IMAP  ->  Microsoft 365", 300);
            _rM365Imap = MakeRadio("Microsoft 365  ->  IMAP", 570);
            _rImapImap.Checked = true;
            typeGroup.Controls.Add(_rImapImap);
            typeGroup.Controls.Add(_rImapM365);
            typeGroup.Controls.Add(_rM365Imap);
            _rImapImap.CheckedChanged += delegate { if (_rImapImap.Checked) ApplyTransferType(TransferType.ImapToImap); };
            _rImapM365.CheckedChanged += delegate { if (_rImapM365.Checked) ApplyTransferType(TransferType.ImapToMicrosoft365); };
            _rM365Imap.CheckedChanged += delegate { if (_rM365Imap.Checked) ApplyTransferType(TransferType.Microsoft365ToImap); };

            // Source.
            _sourceGroup = new ClassicGroupBox();
            _sourceGroup.Text = "Source Mailbox";
            _sourceGroup.SetBounds(10, 66, 414, 224);
            root.Controls.Add(_sourceGroup);
            _source = new EndpointPanel(EndpointRole.Source);
            _source.SetBounds(8, 18, 400, 200);
            _sourceGroup.Controls.Add(_source);

            // Destination.
            _destGroup = new ClassicGroupBox();
            _destGroup.Text = "Destination Mailbox";
            _destGroup.SetBounds(432, 66, 414, 224);
            root.Controls.Add(_destGroup);
            _dest = new EndpointPanel(EndpointRole.Destination);
            _dest.SetBounds(8, 18, 400, 200);
            _destGroup.Controls.Add(_dest);

            // Options.
            ClassicGroupBox optGroup = new ClassicGroupBox();
            optGroup.Text = "Transfer Options";
            optGroup.SetBounds(10, 298, 836, 150);
            root.Controls.Add(optGroup);
            BuildOptions(optGroup);

            // Start button.
            _startButton = new ClassicButton();
            _startButton.Text = "Start Transfer";
            _startButton.SetBounds((866 - 160) / 2, 456, 160, 26);
            _startButton.IsDefaultButton = true;
            _startButton.Click += delegate { StartTransfer(); };
            root.Controls.Add(_startButton);

            // Status bar.
            _statusBar = new Win95StatusBar();
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 20;
            ClientSurface.Controls.Add(_statusBar);

            // Menu last so docking leaves the fill panel the remaining space.
            BuildMenuBar(_menu);
        }

        private ClassicRadioButton MakeRadio(string text, int x)
        {
            ClassicRadioButton r = new ClassicRadioButton();
            r.Text = text;
            r.GroupName = "direction";
            r.SetBounds(x, 22, 260, 16);
            return r;
        }

        private void BuildOptions(ClassicGroupBox group)
        {
            _optCopyAll = AddOpt(group, "Copy all folders", 14, 20);
            _optCopyAll.Checked = true;
            _optCopyAll.CheckedChanged += delegate { _selectFoldersButton.Enabled = !_optCopyAll.Checked; };

            _optDates = AddOpt(group, "Preserve message dates", 14, 42);
            _optDates.Checked = true;
            _optFlags = AddOpt(group, "Preserve message flags", 14, 64);
            _optFlags.Checked = true;
            _optSkipDup = AddOpt(group, "Skip duplicate messages", 14, 86);
            _optSkipDup.Checked = true;

            _optAutomap = AddOpt(group, "Automatic folder mapping (Sent / Trash / Junk)", 300, 20);
            _optAutomap.Checked = true;
            _optSubscribe = AddOpt(group, "Subscribe to destination folders", 300, 42);
            _optSubscribe.Checked = true;
            _optDryRun = AddOpt(group, "Dry run (simulation - no changes)", 300, 64);
            _optDryRun.CheckedChanged += delegate { SyncDryRunMenu(); };

            _optDeleteExtra = AddOpt(group, "Delete messages at destination not present at source", 300, 86);
            _optDeleteExtra.Width = 400;
            _optDeleteExtra.ForeColor = Win95Style.StatusFail;
            _optDeleteExtra.CheckedChanged += delegate { ConfirmDestructive(); };

            _selectFoldersButton = new ClassicButton();
            _selectFoldersButton.Text = "Select Folders...";
            _selectFoldersButton.SetBounds(640, 18, 170, 23);
            _selectFoldersButton.Enabled = false;
            _selectFoldersButton.Click += delegate { SelectFolders(); };
            group.Controls.Add(_selectFoldersButton);

            _folderMapButton = new ClassicButton();
            _folderMapButton.Text = "Folder Mapping...";
            _folderMapButton.SetBounds(640, 46, 170, 23);
            _folderMapButton.Click += delegate { EditFolderMappings(); };
            group.Controls.Add(_folderMapButton);

            ClassicLabel warn = new ClassicLabel();
            warn.SetBounds(640, 74, 182, 30);
            warn.WordWrap = true;
            warn.TextColorOverride = Color.FromArgb(0x40, 0x40, 0x40);
            warn.Text = "Destructive options need confirmation.";
            group.Controls.Add(warn);
        }

        private ClassicCheckBox AddOpt(Control parent, string text, int x, int y)
        {
            ClassicCheckBox c = new ClassicCheckBox();
            c.Text = text;
            c.SetBounds(x, y, 280, 16);
            parent.Controls.Add(c);
            return c;
        }

        // --- Transfer type --------------------------------------------------------

        private TransferType _type = TransferType.ImapToImap;

        private void ApplyTransferType(TransferType type)
        {
            _type = type;
            switch (type)
            {
                case TransferType.ImapToMicrosoft365:
                    _source.SetMode(AuthMode.Password);
                    _dest.SetMode(AuthMode.OAuth2);
                    break;
                case TransferType.Microsoft365ToImap:
                    _source.SetMode(AuthMode.OAuth2);
                    _dest.SetMode(AuthMode.Password);
                    break;
                default:
                    _source.SetMode(AuthMode.Password);
                    _dest.SetMode(AuthMode.Password);
                    break;
            }
            UpdateStatus();
        }

        // --- Options helpers ------------------------------------------------------

        private void SyncDryRunMenu()
        {
            _dryRunMenuItem.Checked = _optDryRun.Checked;
        }

        private void ConfirmDestructive()
        {
            if (!_optDeleteExtra.Checked)
                return;

            bool ok = Win95MessageBox.Ask(this, "Destructive Option",
                "Enable deletion of destination messages that are not present at the source?",
                "This permanently removes messages from the DESTINATION mailbox during the " +
                "transfer. The source mailbox is not affected. Only enable this for a true " +
                "mirror/cut-over and after testing.");
            if (!ok)
            {
                _optDeleteExtra.Checked = false;
            }
        }

        private void SelectFolders()
        {
            string v = _source.Validate();
            if (v != null && _source.Mode != AuthMode.OAuth2)
            {
                Win95MessageBox.ShowWarning(this, "Select Folders", v);
                return;
            }
            if (_source.Mode == AuthMode.OAuth2 && !_source.IsAuthenticated)
            {
                Win95MessageBox.ShowWarning(this, "Select Folders", "Sign in to the source Microsoft 365 account first.");
                return;
            }

            _source.EnsureFreshToken();
            using (FolderSelectForm dlg = new FolderSelectForm(_source.GetEndpoint(), _selectedFolders))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Accepted)
                {
                    _selectedFolders = dlg.SelectedFolders ?? new List<string>();
                    UpdateStatus();
                }
            }
        }

        private void EditFolderMappings()
        {
            using (FolderMappingForm dlg = new FolderMappingForm(_folderMappings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Accepted)
                    _folderMappings = dlg.Mappings ?? new List<FolderMapping>();
            }
        }

        // --- Job building ---------------------------------------------------------

        private TransferOptions CollectOptions()
        {
            TransferOptions o = new TransferOptions();
            o.CopyAllFolders = _optCopyAll.Checked;
            o.PreserveDates = _optDates.Checked;
            o.PreserveFlags = _optFlags.Checked;
            o.SkipDuplicates = _optSkipDup.Checked;
            o.SyncFolders = _optAutomap.Checked;
            o.CreateMissingFolders = _optSubscribe.Checked;
            o.DryRun = _optDryRun.Checked;
            o.DeleteExtraOnDestination = _optDeleteExtra.Checked;
            o.SelectedFolders = new List<string>(_selectedFolders);
            o.FolderMappings = new List<FolderMapping>(_folderMappings);
            return o;
        }

        private void ApplyOptions(TransferOptions o)
        {
            if (o == null) return;
            _optCopyAll.Checked = o.CopyAllFolders;
            _optDates.Checked = o.PreserveDates;
            _optFlags.Checked = o.PreserveFlags;
            _optSkipDup.Checked = o.SkipDuplicates;
            _optAutomap.Checked = o.SyncFolders;
            _optSubscribe.Checked = o.CreateMissingFolders;
            _optDryRun.Checked = o.DryRun;
            _optDeleteExtra.Checked = false;   // never auto-enable a destructive option
            _selectedFolders = new List<string>(o.SelectedFolders ?? new List<string>());
            _folderMappings = new List<FolderMapping>(o.FolderMappings ?? new List<FolderMapping>());
            _selectFoldersButton.Enabled = !o.CopyAllFolders;
        }

        private TransferJob BuildJob()
        {
            TransferJob job = new TransferJob();
            job.Name = string.IsNullOrEmpty(_currentJobName) ? "Unsaved job" : _currentJobName;
            job.Type = _type;
            job.Source = _source.GetEndpoint();
            job.Destination = _dest.GetEndpoint();
            job.Options = CollectOptions();
            return job;
        }

        // --- Start ----------------------------------------------------------------

        private void StartTransfer()
        {
            ImapSyncLocation loc = ImapSyncLocator.Locate();
            if (!loc.Found)
            {
                bool open = Win95MessageBox.Ask(this, "imapsync Required",
                    "imapsync was not found. Configure it now?",
                    "Mail Transfer Utility 95 uses imapsync as its transfer backend. It is a " +
                    "separate third-party tool (imapsync.lamiral.info) and is not bundled. " +
                    "Open Tools -> Configure imapsync to set its path?");
                if (open) ShowSettings(0);
                return;
            }

            string sv = _source.Validate();
            if (sv != null) { Win95MessageBox.ShowWarning(this, "Start Transfer", sv); return; }
            string dv = _dest.Validate();
            if (dv != null) { Win95MessageBox.ShowWarning(this, "Start Transfer", dv); return; }

            if (!_source.EnsureFreshToken() || !_dest.EnsureFreshToken())
            {
                Win95MessageBox.ShowWarning(this, "Start Transfer",
                    "The Microsoft 365 sign-in has expired. Sign in again before starting.");
                return;
            }

            TransferJob job = BuildJob();

            string confirm = "Start the transfer now?\r\n\r\n" + job.DirectionText();
            if (job.Options.DryRun) confirm += "\r\nMode: SIMULATION (dry run)";
            if (job.Options.DeleteExtraOnDestination)
                confirm += "\r\nWARNING: destination messages not present at source will be DELETED.";

            bool go = Win95MessageBox.Ask(this, "Start Transfer", "Ready to start.", confirm);
            if (!go) return;

            using (TransferProgressForm dlg = new TransferProgressForm(job))
                dlg.ShowDialog(this);

            UpdateStatus();
        }

        // --- Batch ----------------------------------------------------------------

        private void OpenBatch()
        {
            // The batch shares this window's direction, servers and options; each account
            // supplies only its own credentials.
            TransferEndpoint src = _source.GetEndpoint();
            TransferEndpoint dst = _dest.GetEndpoint();
            src.Username = null; src.RuntimePassword = null; src.RuntimeAccessToken = null; src.RuntimeRefreshToken = null;
            dst.Username = null; dst.RuntimePassword = null; dst.RuntimeAccessToken = null; dst.RuntimeRefreshToken = null;

            if (string.IsNullOrEmpty(src.Host) || string.IsNullOrEmpty(dst.Host))
            {
                Win95MessageBox.ShowWarning(this, "Batch Transfer",
                    "Set the source and destination servers and options on the main window first.",
                    "The batch applies this window's direction, servers and options to every " +
                    "account; only per-account credentials are entered in the batch list.");
                return;
            }

            BatchContext ctx = new BatchContext
            {
                Type = _type,
                SourceTemplate = src,
                DestTemplate = dst,
                Options = CollectOptions(),
                JobBaseName = string.IsNullOrEmpty(_currentJobName) ? "Batch" : _currentJobName
            };

            using (BatchForm dlg = new BatchForm(ctx))
                dlg.ShowDialog(this);

            UpdateStatus();
        }

        // --- Jobs -----------------------------------------------------------------

        private void NewTransfer()
        {
            _currentJobName = null;
            _selectedFolders = new List<string>();
            _folderMappings = new List<FolderMapping>();
            _rImapImap.Checked = true;
            ApplyTransferType(TransferType.ImapToImap);
            _source.LoadEndpoint(new TransferEndpoint());
            _dest.LoadEndpoint(new TransferEndpoint());
            ApplyOptions(new TransferOptions());
            UpdateStatus();
        }

        private void OpenJob()
        {
            using (JobManagerForm dlg = new JobManagerForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ChosenJob != null)
                {
                    LoadJob(dlg.ChosenJob);
                    if (dlg.RunImmediately)
                        StartTransfer();
                }
            }
        }

        private void LoadJob(TransferJob job)
        {
            _currentJobName = job.Name;
            _type = job.Type;

            switch (job.Type)
            {
                case TransferType.ImapToMicrosoft365: _rImapM365.Checked = true; break;
                case TransferType.Microsoft365ToImap: _rM365Imap.Checked = true; break;
                default: _rImapImap.Checked = true; break;
            }
            ApplyTransferType(job.Type);

            _source.LoadEndpoint(job.Source);
            _dest.LoadEndpoint(job.Destination);
            ApplyOptions(job.Options);

            // Load any opted-in stored passwords.
            TryLoadStoredSecret(job.Name, EndpointRole.Source, _source, job.Source);
            TryLoadStoredSecret(job.Name, EndpointRole.Destination, _dest, job.Destination);

            TitleBarControl.Text = "Mail Transfer Utility 95 - " + job.Name;
            TitleBarControl.Invalidate();
            UpdateStatus();
        }

        private void TryLoadStoredSecret(string jobName, EndpointRole role, EndpointPanel panel, TransferEndpoint ep)
        {
            if (ep == null || ep.Auth == AuthMode.OAuth2) return;
            string key = CredentialStore.KeyFor(jobName, role);
            string secret = CredentialStore.Load(key);
            if (!string.IsNullOrEmpty(secret))
            {
                ep.RuntimePassword = secret;
                panel.LoadEndpoint(ep);   // re-apply with the password filled in
            }
        }

        private void SaveJob(bool saveAs)
        {
            string name = _currentJobName;
            if (saveAs || string.IsNullOrEmpty(name))
            {
                name = Win95Prompt.Ask(this, "Save Job", "Job name:", name ?? "Customer Mail Migration");
                if (string.IsNullOrEmpty(name))
                    return;
            }

            TransferJob job = BuildJob();
            job.Name = name;

            // Ask about remembering passwords (opt-in, DPAPI-protected).
            bool remember = false;
            bool hasPasswords = (_source.Mode != AuthMode.OAuth2 && !string.IsNullOrEmpty(job.Source.RuntimePassword)) ||
                                (_dest.Mode != AuthMode.OAuth2 && !string.IsNullOrEmpty(job.Destination.RuntimePassword));
            if (hasPasswords)
            {
                remember = Win95MessageBox.Ask(this, "Save Job",
                    "Remember the password(s) for this job?",
                    "Passwords are encrypted with Windows DPAPI (your account only) and never " +
                    "written as plain text. Choose No to keep them out of storage entirely " +
                    "(recommended) - you will enter them each time the job runs.");
            }

            JobRepository.Upsert(job);

            if (_source.Mode != AuthMode.OAuth2)
                StoreOrClearSecret(name, EndpointRole.Source, remember ? job.Source.RuntimePassword : null);
            if (_dest.Mode != AuthMode.OAuth2)
                StoreOrClearSecret(name, EndpointRole.Destination, remember ? job.Destination.RuntimePassword : null);

            _currentJobName = name;
            TitleBarControl.Text = "Mail Transfer Utility 95 - " + name;
            TitleBarControl.Invalidate();
            SetStatus("Job saved: " + name);
        }

        private void StoreOrClearSecret(string jobName, EndpointRole role, string secret)
        {
            string key = CredentialStore.KeyFor(jobName, role);
            if (string.IsNullOrEmpty(secret))
                CredentialStore.Delete(key);
            else
                CredentialStore.Save(key, secret);
        }

        // --- Tools / Help ---------------------------------------------------------

        private void ShowSettings(int tab)
        {
            using (SettingsForm dlg = new SettingsForm(tab))
                dlg.ShowDialog(this);

            _loggingMenuItem.Checked = Settings.LoggingEnabled;
            UpdateStatus();
        }

        private void ToggleLogging()
        {
            Settings.LoggingEnabled = !Settings.LoggingEnabled;
            Settings.Save();
            _loggingMenuItem.Checked = Settings.LoggingEnabled;
            SetStatus(Settings.LoggingEnabled ? "Logging enabled." : "Logging disabled.");
        }

        private void ViewLogs()
        {
            try
            {
                string path = Log.LogPath;
                if (File.Exists(path))
                {
                    Process.Start(path);
                }
                else
                {
                    Win95MessageBox.ShowInfo(this, "View Logs",
                        Settings.LoggingEnabled
                            ? "No log file has been written yet."
                            : "Logging is disabled. Enable it in Tools -> Enable Logging.");
                }
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowWarning(this, "View Logs", "Could not open the log.", ex.Message);
            }
        }

        private void OpenWorkingFolder()
        {
            try
            {
                Settings.EnsureWorkingDirectory();
                Process.Start(Settings.EffectiveWorkingDirectory);
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowWarning(this, "Open Working Folder", "Could not open the folder.", ex.Message);
            }
        }

        private void ShowDocumentation()
        {
            try
            {
                string readme = Path.Combine(AppContext.BaseDirectory, "README.md");
                if (File.Exists(readme))
                {
                    Process.Start(readme);
                    return;
                }
            }
            catch { }

            Win95MessageBox.ShowInfo(this, "Documentation",
                "See README.md in the application folder for full documentation.",
                "Transfer backend: imapsync (imapsync.lamiral.info).\r\n" +
                "Microsoft 365 requires OAuth 2.0 / Modern Authentication - Basic Auth is not " +
                "supported by Exchange Online for IMAP.");
        }

        private void ShowAbout()
        {
            using (AboutForm dlg = new AboutForm())
                dlg.ShowDialog(this);
        }

        // --- Status ---------------------------------------------------------------

        private void SetStatus(string primary)
        {
            if (_statusBar != null)
                _statusBar.SetPanes(primary, ImapSyncLocator.StatusText());
        }

        private void UpdateStatus()
        {
            string folders = _optCopyAll != null && !_optCopyAll.Checked
                ? _selectedFolders.Count + " folder(s) selected"
                : "All folders";
            SetStatus("Ready.   " + TransferJob.DirectionText(_type) + "   -   " + folders);
        }
    }
}
