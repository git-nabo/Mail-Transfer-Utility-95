using System;
using System.Drawing;
using System.Windows.Forms;

namespace MailTransfer95.Ui
{
    /// <summary>A simple single-line input dialog in the Win95 style.</summary>
    public sealed class Win95Prompt : Win95Form
    {
        private readonly TextBox _input;

        public string Value { get { return _input.Text.Trim(); } }

        public Win95Prompt(string caption, string message, string initial)
        {
            BuildChrome(caption);
            CloseOnEscape = true;
            Width = 400;
            Height = 170;
            StartPosition = FormStartPosition.CenterParent;

            ClassicLabel label = new ClassicLabel();
            label.Text = message;
            label.SetBounds(16, 16, 360, 16);
            ClientSurface.Controls.Add(label);

            SunkenPanel host = new SunkenPanel();
            host.SetBounds(16, 40, 360, 21);
            ClientSurface.Controls.Add(host);

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.Font = Win95Style.CreateFontSafe();
            _input.BackColor = Win95Style.FieldBack;
            _input.Text = initial ?? string.Empty;
            _input.SetBounds(4, 3, 352, 15);
            host.Controls.Add(_input);

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
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            bar.Controls.Add(ok);

            ClassicButton cancel = new ClassicButton();
            cancel.Text = "Cancel";
            cancel.SetBounds(Width - 104, 9, 86, 23);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.IsCancelButton = true;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            bar.Controls.Add(cancel);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _input.Focus();
            _input.SelectAll();
        }

        /// <summary>Shows the prompt and returns the entered text, or null if cancelled.</summary>
        public static string Ask(IWin32Window owner, string caption, string message, string initial)
        {
            using (Win95Prompt p = new Win95Prompt(caption, message, initial))
            {
                if (p.ShowDialog(owner) == DialogResult.OK && p.Value.Length > 0)
                    return p.Value;
                return null;
            }
        }
    }
}
