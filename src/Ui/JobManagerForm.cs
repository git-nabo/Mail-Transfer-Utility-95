using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Lists saved migration jobs (profiles). A job can be opened into the main window
    /// or run directly. Jobs never contain passwords or tokens; those are entered at run
    /// time (or, only if the user opted in, loaded from the DPAPI credential store).
    /// </summary>
    public sealed class JobManagerForm : Win95Form
    {
        private ClassicListView _list;
        private List<TransferJob> _jobs;

        public TransferJob ChosenJob { get; private set; }
        public bool RunImmediately { get; private set; }

        public JobManagerForm()
        {
            BuildChrome("Saved Jobs");
            CloseOnEscape = true;
            Width = 560;
            Height = 380;
            StartPosition = FormStartPosition.CenterParent;

            Build();
            Reload();
        }

        private void Build()
        {
            ClassicLabel intro = new ClassicLabel();
            intro.SetBounds(12, 8, 520, 16);
            intro.Text = "Saved migration jobs. Credentials are entered when the job runs.";
            ClientSurface.Controls.Add(intro);

            _list = new ClassicListView();
            _list.ShowHeader = true;
            _list.Columns.Add(new ListColumn("Name", 150));
            _list.Columns.Add(new ListColumn("Direction", 150));
            _list.Columns.Add(new ListColumn("Source", 110));
            _list.Columns.Add(new ListColumn("Destination", 110));
            _list.SetBounds(12, 28, 532, 260);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.ItemActivate += delegate { Open(); };
            ClientSurface.Controls.Add(_list);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 40;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            ClassicButton open = new ClassicButton();
            open.Text = "Open";
            open.SetBounds(12, 8, 86, 23);
            open.Click += delegate { Open(); };
            bar.Controls.Add(open);

            ClassicButton run = new ClassicButton();
            run.Text = "Run";
            run.SetBounds(104, 8, 86, 23);
            run.Click += delegate { Run(); };
            bar.Controls.Add(run);

            ClassicButton del = new ClassicButton();
            del.Text = "Delete";
            del.SetBounds(196, 8, 86, 23);
            del.Click += delegate { Delete(); };
            bar.Controls.Add(del);

            ClassicButton close = new ClassicButton();
            close.Text = "Close";
            close.SetBounds(Width - 104, 8, 86, 23);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.IsCancelButton = true;
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);
        }

        private void Reload()
        {
            _jobs = JobRepository.LoadAll();
            List<ListRow> rows = new List<ListRow>();
            foreach (TransferJob j in _jobs)
            {
                ListRow r = new ListRow(
                    j.Name ?? "(unnamed)",
                    j.DirectionText(),
                    HostOf(j.Source),
                    HostOf(j.Destination));
                r.Tag = j;
                rows.Add(r);
            }
            _list.SetRows(rows);
        }

        private static string HostOf(TransferEndpoint ep)
        {
            if (ep == null) return "-";
            return string.IsNullOrEmpty(ep.Host) ? "-" : ep.Host;
        }

        private TransferJob Selected()
        {
            ListRow r = _list.SelectedRow;
            return r != null ? r.Tag as TransferJob : null;
        }

        private void Open()
        {
            TransferJob j = Selected();
            if (j == null) { Warn(); return; }
            ChosenJob = j;
            RunImmediately = false;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Run()
        {
            TransferJob j = Selected();
            if (j == null) { Warn(); return; }
            ChosenJob = j;
            RunImmediately = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Delete()
        {
            TransferJob j = Selected();
            if (j == null) { Warn(); return; }

            bool yes = Win95MessageBox.Ask(this, "Delete Job",
                "Delete the saved job \"" + j.Name + "\"?",
                "This removes the saved connection settings. Mailboxes are not affected.");
            if (!yes) return;

            JobRepository.Delete(j.Name);
            try { CredentialStore.Delete(CredentialStore.KeyFor(j.Name, EndpointRole.Source)); } catch { }
            try { CredentialStore.Delete(CredentialStore.KeyFor(j.Name, EndpointRole.Destination)); } catch { }
            Reload();
        }

        private void Warn()
        {
            Win95MessageBox.ShowWarning(this, "Saved Jobs", "Select a job first.");
        }
    }
}
