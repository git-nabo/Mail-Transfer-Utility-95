using System;
using System.Drawing;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// The end-of-run migration report: a headline, the folder/message/error counts and
    /// the readable exit explanation, with Copy Summary and Save Report actions. Errors
    /// are translated into plain language rather than a bare exit code.
    /// </summary>
    public sealed class ReportForm : Win95Form
    {
        private readonly MigrationReport _report;

        public ReportForm(MigrationReport report)
        {
            _report = report;

            BuildChrome("Migration Report");
            CloseOnEscape = true;
            Width = 500;
            Height = 400;
            StartPosition = FormStartPosition.CenterParent;

            Build();
        }

        private void Build()
        {
            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 54;
            head.BackColor = Win95Style.Face;
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                Win95Style.PrepareGraphics(e.Graphics);
                Win95Icon icon = _report.Success
                    ? (_report.DryRun ? Win95Icon.Information : Win95Icon.Information)
                    : (_report.Cancelled ? Win95Icon.Warning : Win95Icon.Warning);
                Win95MessageBox.DrawIcon(e.Graphics, 14, 12, icon);
                TextRenderer.DrawText(e.Graphics, _report.HeadlineText, Win95Style.BoldFont,
                    new Rectangle(58, 18, Width - 80, 18),
                    _report.Success ? Win95Style.StatusOk : Win95Style.StatusFail,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            };
            ClientSurface.Controls.Add(head);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 40;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            ClassicButton copy = new ClassicButton();
            copy.Text = "Copy Summary";
            copy.SetBounds(14, 8, 120, 23);
            copy.Click += delegate { CopySummary(); };
            bar.Controls.Add(copy);

            ClassicButton save = new ClassicButton();
            save.Text = "Save Report...";
            save.SetBounds(142, 8, 120, 23);
            save.Click += delegate { SaveReport(); };
            bar.Controls.Add(save);

            ClassicButton close = new ClassicButton();
            close.Text = "Close";
            close.SetBounds(Width - 114, 8, 96, 23);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.IsDefaultButton = true;
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);

            SunkenPanel host = new SunkenPanel();
            host.Dock = DockStyle.Fill;
            ClientSurface.Controls.Add(host);
            host.BringToFront();
            head.BringToFront();

            TextBox text = new TextBox();
            text.Multiline = true;
            text.ReadOnly = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.BorderStyle = BorderStyle.None;
            text.BackColor = Win95Style.FieldBack;
            text.Font = Win95Style.CreateMonospaceFont();
            text.Dock = DockStyle.Fill;
            text.Text = _report.ToText().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            text.Select(0, 0);
            host.Controls.Add(text);
        }

        private void CopySummary()
        {
            try
            {
                Clipboard.SetText(_report.ToText());
                TitleBarControl.Text = "Migration Report  -  copied";
                TitleBarControl.Invalidate();
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowWarning(this, "Copy Summary", "Could not copy to the clipboard.", ex.Message);
            }
        }

        private void SaveReport()
        {
            try
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "Save Migration Report";
                    dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                    dlg.FileName = "migration-report.txt";
                    dlg.InitialDirectory = Settings.EffectiveWorkingDirectory;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        System.IO.File.WriteAllText(dlg.FileName, _report.ToText(),
                            new System.Text.UTF8Encoding(false));
                        Win95MessageBox.ShowInfo(this, "Save Report", "Report saved.", dlg.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowError(this, "Save Report", "The report could not be saved.", ex.Message);
            }
        }
    }
}
