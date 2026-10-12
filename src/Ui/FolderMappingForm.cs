using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Advanced folder-name mapping (source name -&gt; destination name). Optional: the
    /// default behaviour is automatic, so simple migrations never need this. Provides a
    /// one-click standard Exchange mapping (Sent -&gt; Sent Items, Trash -&gt; Deleted
    /// Items, Junk -&gt; Junk Email).
    /// </summary>
    public sealed class FolderMappingForm : Win95Form
    {
        private ClassicListView _list;
        private TextBox _src;
        private TextBox _dst;

        public List<FolderMapping> Mappings { get; private set; }
        public bool Accepted { get; private set; }

        public FolderMappingForm(List<FolderMapping> existing)
        {
            Mappings = new List<FolderMapping>();
            if (existing != null)
                foreach (FolderMapping m in existing)
                    Mappings.Add(new FolderMapping(m.Source, m.Destination));

            BuildChrome("Folder Mapping");
            CloseOnEscape = true;
            Width = 440;
            Height = 380;
            StartPosition = FormStartPosition.CenterParent;

            Build();
            Refresh2();
        }

        private void Build()
        {
            ClassicLabel intro = new ClassicLabel();
            intro.SetBounds(12, 8, 410, 16);
            intro.Text = "Translate folder names during the transfer. Optional.";
            ClientSurface.Controls.Add(intro);

            _list = new ClassicListView();
            _list.ShowHeader = true;
            _list.Columns.Add(new ListColumn("Source", 190));
            _list.Columns.Add(new ListColumn("Destination", 190));
            _list.SetBounds(12, 28, 412, 200);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            ClientSurface.Controls.Add(_list);

            SunkenPanel sh = new SunkenPanel();
            sh.SetBounds(12, 236, 150, 21);
            ClientSurface.Controls.Add(sh);
            _src = Field(sh);
            _src.Text = "";

            ClassicLabel arrow = new ClassicLabel();
            arrow.Text = "->";
            arrow.TextAlign = ContentAlignment.MiddleCenter;
            arrow.SetBounds(166, 236, 20, 18);
            ClientSurface.Controls.Add(arrow);

            SunkenPanel dh = new SunkenPanel();
            dh.SetBounds(190, 236, 150, 21);
            ClientSurface.Controls.Add(dh);
            _dst = Field(dh);

            ClassicButton add = new ClassicButton();
            add.Text = "Add";
            add.SetBounds(348, 235, 76, 23);
            add.Click += delegate { AddMapping(); };
            ClientSurface.Controls.Add(add);

            ClassicButton remove = new ClassicButton();
            remove.Text = "Remove";
            remove.SetBounds(12, 268, 90, 23);
            remove.Click += delegate { RemoveSelected(); };
            ClientSurface.Controls.Add(remove);

            ClassicButton std = new ClassicButton();
            std.Text = "Add Standard (Exchange)";
            std.SetBounds(110, 268, 180, 23);
            std.Click += delegate { AddStandard(); };
            ClientSurface.Controls.Add(std);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 40;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

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

        private static TextBox Field(SunkenPanel host)
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.None;
            t.Font = Win95Style.CreateFontSafe();
            t.BackColor = Win95Style.FieldBack;
            t.SetBounds(4, 3, host.Width - 8, 15);
            host.Controls.Add(t);
            return t;
        }

        private void Refresh2()
        {
            List<ListRow> rows = new List<ListRow>();
            foreach (FolderMapping m in Mappings)
                rows.Add(new ListRow(m.Source, m.Destination));
            _list.SetRows(rows);
        }

        private void AddMapping()
        {
            string s = _src.Text.Trim();
            string d = _dst.Text.Trim();
            if (s.Length == 0 || d.Length == 0)
            {
                Win95MessageBox.ShowWarning(this, "Folder Mapping",
                    "Enter both a source and a destination folder name.");
                return;
            }
            Mappings.Add(new FolderMapping(s, d));
            _src.Text = _dst.Text = string.Empty;
            Refresh2();
        }

        private void AddStandard()
        {
            AddIfMissing("Sent", "Sent Items");
            AddIfMissing("Trash", "Deleted Items");
            AddIfMissing("Junk", "Junk Email");
            Refresh2();
        }

        private void AddIfMissing(string s, string d)
        {
            foreach (FolderMapping m in Mappings)
                if (string.Equals(m.Source, s, StringComparison.OrdinalIgnoreCase))
                    return;
            Mappings.Add(new FolderMapping(s, d));
        }

        private void RemoveSelected()
        {
            int idx = _list.SelectedIndex;
            if (idx >= 0 && idx < Mappings.Count)
            {
                Mappings.RemoveAt(idx);
                Refresh2();
            }
        }

        private void Accept()
        {
            Accepted = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
