using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Lets the user choose a subset of source folders to migrate. Folders are listed
    /// live from the source mailbox (read-only). Falls back to a standard folder set if
    /// the mailbox cannot be listed.
    /// </summary>
    public sealed class FolderSelectForm : Win95Form
    {
        private readonly TransferEndpoint _source;
        private readonly List<string> _preselected;
        private ClassicListView _list;
        private ClassicLabel _status;

        public List<string> SelectedFolders { get; private set; }
        public bool Accepted { get; private set; }

        private static readonly string[] CommonFolders =
            { "INBOX", "Sent", "Drafts", "Trash", "Junk", "Archive" };

        public FolderSelectForm(TransferEndpoint source, List<string> preselected)
        {
            _source = source;
            _preselected = preselected ?? new List<string>();

            BuildChrome("Select Folders");
            CloseOnEscape = true;
            Width = 380;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;

            Build();
        }

        private void Build()
        {
            _status = new ClassicLabel();
            _status.SetBounds(12, 8, 350, 16);
            _status.Text = "Listing folders from the source mailbox...";
            ClientSurface.Controls.Add(_status);

            _list = new ClassicListView();
            _list.ShowCheckBoxes = true;
            _list.Columns.Add(new ListColumn("Folder", 320));
            _list.SetBounds(12, 28, 352, 300);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            ClientSurface.Controls.Add(_list);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 40;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            ClassicButton all = new ClassicButton();
            all.Text = "Select All";
            all.SetBounds(12, 8, 86, 23);
            all.Click += delegate { SetAll(true); };
            bar.Controls.Add(all);

            ClassicButton none = new ClassicButton();
            none.Text = "Clear";
            none.SetBounds(104, 8, 70, 23);
            none.Click += delegate { SetAll(false); };
            bar.Controls.Add(none);

            ClassicButton ok = new ClassicButton();
            ok.Text = "OK";
            ok.SetBounds(Width - 196, 8, 86, 23);
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ok.IsDefaultButton = true;
            ok.Click += delegate { Accept(); };
            bar.Controls.Add(ok);

            ClassicButton cancel = new ClassicButton();
            cancel.Text = "Cancel";
            cancel.SetBounds(Width - 104, 8, 86, 23);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.IsCancelButton = true;
            cancel.Click += delegate { Close(); };
            bar.Controls.Add(cancel);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Thread t = new Thread(LoadFolders) { IsBackground = true };
            t.Start();
        }

        private void LoadFolders()
        {
            FolderListResult r = FolderService.ListFolders(_source, Settings.TimeoutSeconds);
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    List<string> folders;
                    if (r.Success && r.Folders.Count > 0)
                    {
                        folders = r.Folders;
                        _status.Text = r.Folders.Count + " folders found.";
                        _status.TextColorOverride = Win95Style.StatusOk;
                    }
                    else
                    {
                        folders = new List<string>(CommonFolders);
                        _status.Text = "Could not list folders (" + (r.Error ?? "unknown") +
                                       "). Showing a standard set.";
                        _status.TextColorOverride = Win95Style.StatusFail;
                    }
                    Populate(folders);
                    _status.Invalidate();
                }));
            }
            catch { }
        }

        private void Populate(List<string> folders)
        {
            List<ListRow> rows = new List<ListRow>();
            foreach (string f in folders)
            {
                ListRow row = new ListRow(f);
                row.Checkable = true;
                row.Checked = _preselected.Count == 0 || _preselected.Contains(f);
                rows.Add(row);
            }
            _list.SetRows(rows);
        }

        private void SetAll(bool check)
        {
            foreach (ListRow r in _list.Rows)
                if (r.Checkable) r.Checked = check;
            _list.Invalidate();
        }

        private void Accept()
        {
            List<string> selected = new List<string>();
            foreach (ListRow r in _list.Rows)
                if (r.Checked && r.Cells.Length > 0) selected.Add(r.Cells[0]);

            SelectedFolders = selected;
            Accepted = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
