using System;
using System.Drawing;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>Add or edit a single account in a batch. Password fields appear only for
    /// the sides that use normal IMAP authentication; OAuth sides sign in at run time.</summary>
    public sealed class BatchAccountForm : Win95Form
    {
        private readonly BatchContext _ctx;
        private readonly BatchAccount _account;

        private TextBox _srcUser;
        private TextBox _srcPass;
        private TextBox _dstUser;
        private TextBox _dstPass;

        public BatchAccount Result { get; private set; }

        public BatchAccountForm(BatchContext ctx, BatchAccount existing)
        {
            _ctx = ctx;
            _account = existing;

            BuildChrome(existing == null ? "Add Account" : "Edit Account");
            CloseOnEscape = true;
            Width = 420;
            Height = 250;
            StartPosition = FormStartPosition.CenterParent;

            Build();
        }

        private void Build()
        {
            int y = 16;

            _srcUser = Row("Source user:", y, false); y += 30;
            if (!_ctx.SourceIsOAuth)
            {
                _srcPass = Row("Source password:", y, true); y += 30;
            }
            else
            {
                Note("Source is Microsoft 365 - sign in at run time.", y); y += 24;
            }

            _dstUser = Row("Dest. user:", y, false); y += 2;
            ClassicLabel dstHint = new ClassicLabel();
            dstHint.SetBounds(130, y + 18, 270, 14);
            dstHint.TextColorOverride = Win95Style.FaceDark;
            dstHint.Text = "Leave blank to reuse the source user.";
            ClientSurface.Controls.Add(dstHint);
            y += 34;

            if (!_ctx.DestIsOAuth)
            {
                _dstPass = Row("Dest. password:", y, true); y += 30;
            }
            else
            {
                Note("Destination is Microsoft 365 - sign in at run time.", y); y += 24;
            }

            if (_account != null)
            {
                _srcUser.Text = _account.SourceUser ?? string.Empty;
                if (_srcPass != null) _srcPass.Text = _account.SourcePassword ?? string.Empty;
                _dstUser.Text = _account.DestUser ?? string.Empty;
                if (_dstPass != null) _dstPass.Text = _account.DestPassword ?? string.Empty;
            }

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

        private TextBox Row(string caption, int y, bool password)
        {
            ClassicLabel l = new ClassicLabel();
            l.Text = caption;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.SetBounds(16, y, 110, 18);
            ClientSurface.Controls.Add(l);

            SunkenPanel host = new SunkenPanel();
            host.SetBounds(130, y - 2, 260, 21);
            ClientSurface.Controls.Add(host);

            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.None;
            t.Font = Win95Style.CreateFontSafe();
            t.BackColor = Win95Style.FieldBack;
            if (password) t.UseSystemPasswordChar = true;
            t.SetBounds(4, 3, 252, 15);
            host.Controls.Add(t);
            return t;
        }

        private void Note(string text, int y)
        {
            ClassicLabel l = new ClassicLabel();
            l.WordWrap = true;
            l.TextColorOverride = Color.FromArgb(0x00, 0x00, 0x80);
            l.SetBounds(16, y, 380, 18);
            l.Text = text;
            ClientSurface.Controls.Add(l);
        }

        private void Accept()
        {
            if (string.IsNullOrEmpty(_srcUser.Text.Trim()))
            {
                Win95MessageBox.ShowWarning(this, "Add Account", "Enter the source user.");
                return;
            }

            BatchAccount a = _account ?? new BatchAccount();
            a.SourceUser = _srcUser.Text.Trim();
            if (_srcPass != null) a.SourcePassword = _srcPass.Text;
            a.DestUser = _dstUser.Text.Trim();
            if (_dstPass != null) a.DestPassword = _dstPass.Text;

            string v = _ctx.Validate(a);
            if (v != null)
            {
                Win95MessageBox.ShowWarning(this, "Add Account", v);
                return;
            }

            Result = a;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
