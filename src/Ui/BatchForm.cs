using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Batch migration: run many mailboxes with one shared configuration (direction,
    /// both servers and the options come from the main window) so each account needs
    /// only its own credentials. Accounts can be added by hand or imported from CSV.
    /// Each account runs through the same single-mailbox transfer pipeline, one after
    /// another, so the batch is just a sequential driver over the proven engine.
    /// Credentials live only in memory and are never saved.
    /// </summary>
    public sealed class BatchForm : Win95Form
    {
        private readonly BatchContext _ctx;
        private readonly List<BatchAccount> _accounts = new List<BatchAccount>();
        private ClassicListView _list;
        private bool _running;

        public BatchForm(BatchContext ctx)
        {
            _ctx = ctx;

            BuildChrome("Batch Transfer");
            CloseOnEscape = true;
            Width = 640;
            Height = 486;
            StartPosition = FormStartPosition.CenterParent;

            Build();
        }

        private void Build()
        {
            ClassicGroupBox shared = new ClassicGroupBox();
            shared.Text = "Shared Settings (from the main window)";
            shared.SetBounds(10, 8, 620, 66);
            ClientSurface.Controls.Add(shared);

            ClassicLabel l1 = new ClassicLabel();
            l1.SetBounds(12, 20, 600, 14);
            l1.Text = "Direction:  " + TransferJob.DirectionText(_ctx.Type);
            shared.Controls.Add(l1);

            ClassicLabel l2 = new ClassicLabel();
            l2.SetBounds(12, 36, 600, 14);
            l2.Text = "Source:  " + Describe(_ctx.SourceTemplate) + "      Destination:  " + Describe(_ctx.DestTemplate);
            shared.Controls.Add(l2);

            ClassicLabel l3 = new ClassicLabel();
            l3.SetBounds(12, 50, 600, 14);
            l3.TextColorOverride = Win95Style.FaceDark;
            l3.Text = "Credentials are entered per account below and are never saved.";
            shared.Controls.Add(l3);

            _list = new ClassicListView();
            _list.ShowCheckBoxes = true;
            _list.ShowHeader = true;
            _list.Columns.Add(new ListColumn("Source account", 220));
            _list.Columns.Add(new ListColumn("Destination account", 200));
            _list.Columns.Add(new ListColumn("Status", 150));
            _list.SetBounds(10, 82, 620, 250);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.ItemActivate += delegate { EditSelected(); };
            ClientSurface.Controls.Add(_list);

            // Account management buttons.
            int by = 340;
            AddButton("Add...", 10, by, 80, delegate { AddAccount(); });
            AddButton("Edit...", 94, by, 80, delegate { EditSelected(); });
            AddButton("Remove", 178, by, 80, delegate { RemoveSelected(); });
            AddButton("Import CSV...", 280, by, 110, delegate { ImportCsv(); });
            AddButton("CSV Template...", 394, by, 120, delegate { SaveTemplate(); });

            // Run / close bar.
            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 44;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            ClassicButton runSel = new ClassicButton();
            runSel.Text = "Run Selected";
            runSel.SetBounds(10, 10, 120, 24);
            runSel.Click += delegate { Run(true); };
            bar.Controls.Add(runSel);

            ClassicButton runAll = new ClassicButton();
            runAll.Text = "Run All";
            runAll.SetBounds(138, 10, 110, 24);
            runAll.Click += delegate { Run(false); };
            bar.Controls.Add(runAll);

            ClassicButton close = new ClassicButton();
            close.Text = "Close";
            close.SetBounds(Width - 110, 10, 92, 24);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.IsCancelButton = true;
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);
        }

        private void AddButton(string text, int x, int y, int w, EventHandler click)
        {
            ClassicButton b = new ClassicButton();
            b.Text = text;
            b.SetBounds(x, y, w, 23);
            b.Click += click;
            ClientSurface.Controls.Add(b);
        }

        private static string Describe(TransferEndpoint ep)
        {
            if (ep == null) return "-";
            string auth = ep.Auth == AuthMode.OAuth2 ? " (OAuth)" : "";
            return (string.IsNullOrEmpty(ep.Host) ? "(no host)" : ep.Host) + auth;
        }

        // --- Account list ---------------------------------------------------------

        private void RefreshList()
        {
            List<ListRow> rows = new List<ListRow>();
            foreach (BatchAccount a in _accounts)
            {
                ListRow r = new ListRow(a.SourceUser, a.EffectiveDestUser, a.Status);
                r.Checkable = true;
                r.Checked = a.Enabled;
                r.Tag = a;
                r.ForeOverride = StatusColor(a.Status);
                rows.Add(r);
            }
            int sel = _list.SelectedIndex;
            _list.SetRows(rows);
            if (sel >= 0 && sel < rows.Count) _list.SelectedIndex = sel;
        }

        private static Color? StatusColor(string status)
        {
            if (status == null) return null;
            if (status.StartsWith("Done") || status.StartsWith("Simulated")) return Win95Style.StatusOk;
            if (status.StartsWith("Errors") || status.StartsWith("Failed") ||
                status.StartsWith("Invalid") || status.StartsWith("Cancelled") || status.StartsWith("Skipped"))
                return Win95Style.StatusFail;
            if (status.StartsWith("Running")) return Win95Style.SelectionBack;
            return null;
        }

        private void SyncCheckedState()
        {
            foreach (ListRow r in _list.Rows)
            {
                BatchAccount a = r.Tag as BatchAccount;
                if (a != null) a.Enabled = r.Checked;
            }
        }

        private BatchAccount Selected()
        {
            ListRow r = _list.SelectedRow;
            return r != null ? r.Tag as BatchAccount : null;
        }

        private void AddAccount()
        {
            using (BatchAccountForm dlg = new BatchAccountForm(_ctx, null))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
                {
                    _accounts.Add(dlg.Result);
                    RefreshList();
                }
            }
        }

        private void EditSelected()
        {
            BatchAccount a = Selected();
            if (a == null) return;
            using (BatchAccountForm dlg = new BatchAccountForm(_ctx, a))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    RefreshList();
            }
        }

        private void RemoveSelected()
        {
            BatchAccount a = Selected();
            if (a == null) return;
            _accounts.Remove(a);
            RefreshList();
        }

        // --- CSV ------------------------------------------------------------------

        private void ImportCsv()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Import accounts from CSV";
                dlg.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    string text = File.ReadAllText(dlg.FileName);
                    string error;
                    List<BatchAccount> imported = BatchCsv.Parse(text, out error);
                    if (imported.Count == 0)
                    {
                        Win95MessageBox.ShowWarning(this, "Import CSV", error ?? "No rows found.");
                        return;
                    }

                    _accounts.AddRange(imported);
                    RefreshList();

                    Win95MessageBox.ShowInfo(this, "Import CSV",
                        "Imported " + imported.Count + " account(s).",
                        "Note: the CSV contains plain-text passwords. Nothing is saved by this " +
                        "application. Delete the CSV file when you no longer need it.");
                }
                catch (Exception ex)
                {
                    Win95MessageBox.ShowError(this, "Import CSV", "The file could not be read.", ex.Message);
                }
            }
        }

        private void SaveTemplate()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Save CSV template";
                dlg.Filter = "CSV files (*.csv)|*.csv";
                dlg.FileName = "batch-accounts-template.csv";
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    File.WriteAllText(dlg.FileName, BatchCsv.Template(), new System.Text.UTF8Encoding(false));
                    Win95MessageBox.ShowInfo(this, "CSV Template", "Template saved.", dlg.FileName);
                }
                catch (Exception ex)
                {
                    Win95MessageBox.ShowError(this, "CSV Template", "The template could not be saved.", ex.Message);
                }
            }
        }

        // --- Running --------------------------------------------------------------

        private void Run(bool selectedOnly)
        {
            if (_running) return;
            SyncCheckedState();

            ImapSyncLocation loc = ImapSyncLocator.Locate();
            if (!loc.Found)
            {
                Win95MessageBox.ShowWarning(this, "Batch Transfer",
                    "imapsync was not found. Configure it in Tools -> Configure imapsync first.");
                return;
            }
            if (_ctx.SourceTemplate == null || string.IsNullOrEmpty(_ctx.SourceTemplate.Host) ||
                _ctx.DestTemplate == null || string.IsNullOrEmpty(_ctx.DestTemplate.Host))
            {
                Win95MessageBox.ShowWarning(this, "Batch Transfer",
                    "Set the source and destination servers in the main window before running a batch.");
                return;
            }

            List<BatchAccount> toRun = new List<BatchAccount>();
            foreach (BatchAccount a in _accounts)
                if (!selectedOnly || a.Enabled)
                    toRun.Add(a);

            if (toRun.Count == 0)
            {
                Win95MessageBox.ShowWarning(this, "Batch Transfer",
                    selectedOnly ? "No accounts are checked." : "No accounts to run.");
                return;
            }

            string warn = "Run " + toRun.Count + " mailbox transfer(s), one after another?\r\n\r\n" +
                          TransferJob.DirectionText(_ctx.Type);
            if (_ctx.Options != null && _ctx.Options.DryRun) warn += "\r\nMode: SIMULATION (dry run)";
            if (_ctx.Options != null && _ctx.Options.DeleteExtraOnDestination)
                warn += "\r\nWARNING: destination messages not present at source will be DELETED.";
            if (!Win95MessageBox.Ask(this, "Batch Transfer", "Ready to start the batch.", warn))
                return;

            _running = true;
            try
            {
                RunSequential(toRun);
            }
            finally
            {
                _running = false;
            }
        }

        private void RunSequential(List<BatchAccount> toRun)
        {
            int done = 0, errors = 0, skipped = 0;

            foreach (BatchAccount a in toRun)
            {
                a.Status = "Running...";
                RefreshList();
                Application.DoEvents();

                string v = _ctx.Validate(a);
                if (v != null)
                {
                    a.Status = "Invalid: " + v;
                    skipped++;
                    RefreshList();
                    continue;
                }

                if (!EnsureTokens(a))
                {
                    a.Status = "Skipped (no sign-in)";
                    skipped++;
                    RefreshList();
                    continue;
                }

                TransferJob job = _ctx.BuildJob(a);
                MigrationReport report;
                using (TransferProgressForm dlg = new TransferProgressForm(job))
                {
                    dlg.ShowDialog(this);
                    report = dlg.Report;
                }

                a.LastReport = report;
                if (report == null)
                {
                    a.Status = "Failed";
                    errors++;
                }
                else if (report.Cancelled)
                {
                    a.Status = "Cancelled";
                    RefreshList();
                    if (!Win95MessageBox.Ask(this, "Batch Transfer",
                            "That transfer was cancelled. Continue with the rest of the batch?", null))
                        break;
                    continue;
                }
                else if (report.Success)
                {
                    a.Status = report.DryRun ? "Simulated OK" : "Done";
                    done++;
                }
                else
                {
                    a.Status = "Errors (exit " + report.ExitCode + ")";
                    errors++;
                }

                RefreshList();
            }

            Win95MessageBox.ShowInfo(this, "Batch Transfer",
                "Batch finished.",
                "Succeeded: " + done + "\r\nWith errors: " + errors + "\r\nSkipped: " + skipped);
        }

        /// <summary>
        /// Ensures OAuth tokens for any Microsoft 365 side of this account, prompting an
        /// interactive sign-in for that specific mailbox. Returns false if a required
        /// sign-in was cancelled.
        /// </summary>
        private bool EnsureTokens(BatchAccount a)
        {
            if (_ctx.SourceIsOAuth)
            {
                if (!SignInFor(a.SourceUser, ref a.SourceAccessToken, ref a.SourceRefreshToken, ref a.SourceTokenExpiresUtc))
                    return false;
            }
            if (_ctx.DestIsOAuth)
            {
                string who = a.EffectiveDestUser;
                if (!SignInFor(who, ref a.DestAccessToken, ref a.DestRefreshToken, ref a.DestTokenExpiresUtc))
                    return false;
            }
            return true;
        }

        private bool SignInFor(string user, ref string accessToken, ref string refreshToken, ref DateTime expiresUtc)
        {
            // Reuse a still-valid token, or refresh silently.
            if (!string.IsNullOrEmpty(accessToken) && DateTime.UtcNow < expiresUtc.AddMinutes(-2))
                return true;
            if (!string.IsNullOrEmpty(refreshToken))
            {
                TokenResult rt = OAuthService.RefreshToken(Settings.OAuthTenantId, Settings.OAuthClientId, refreshToken);
                if (rt.Success)
                {
                    accessToken = rt.AccessToken; refreshToken = rt.RefreshToken; expiresUtc = rt.ExpiresUtc;
                    return true;
                }
            }

            if (string.IsNullOrEmpty(Settings.OAuthClientId))
            {
                Win95MessageBox.ShowWarning(this, "Microsoft Sign-in",
                    "No Microsoft 365 Client ID is configured (Tools -> OAuth Settings).");
                return false;
            }

            Win95MessageBox.ShowInfo(this, "Microsoft Sign-in",
                "The next mailbox needs a Microsoft 365 sign-in.",
                "Please sign in for: " + user + "\r\n\r\nMicrosoft 365 delegated OAuth signs in per " +
                "mailbox. (Unattended bulk would require application-only auth, which is not part of " +
                "this version.)");

            using (OAuthSignInForm dlg = new OAuthSignInForm(Settings.OAuthTenantId, Settings.OAuthClientId))
            {
                dlg.ShowDialog(this);
                if (dlg.Result != null && dlg.Result.Success)
                {
                    accessToken = dlg.Result.AccessToken;
                    refreshToken = dlg.Result.RefreshToken;
                    expiresUtc = dlg.Result.ExpiresUtc;
                    return true;
                }
            }
            return false;
        }

        protected override void RequestClose()
        {
            if (_running)
            {
                Win95MessageBox.ShowWarning(this, "Batch Transfer",
                    "A batch is running. Cancel the current transfer first.");
                return;
            }
            base.RequestClose();
        }
    }
}
