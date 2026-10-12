using System;
using System.Drawing;
using System.Windows.Forms;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Help -&gt; About. Shows the application logo (two mailboxes with a transfer arrow),
    /// version and author, styled to match the rest of the utility.
    /// </summary>
    public sealed class AboutForm : Win95Form
    {
        public AboutForm()
        {
            BuildChrome("About Mail Transfer Utility 95");
            CloseOnEscape = true;
            Width = 420;
            Height = 266;
            StartPosition = FormStartPosition.CenterParent;

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Win95Style.Face;
            body.Paint += delegate (object s, PaintEventArgs e) { Paint2(e.Graphics); };
            ClientSurface.Controls.Add(body);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 40;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            ClassicButton ok = new ClassicButton();
            ok.Text = "OK";
            ok.SetBounds(Width - 110, 8, 90, 23);
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ok.IsDefaultButton = true;
            ok.Click += delegate { Close(); };
            bar.Controls.Add(ok);
        }

        private void Paint2(Graphics g)
        {
            Win95Style.PrepareGraphics(g);
            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            DrawLogo(g, 24, 22);

            int x = 96, y = 20;
            Font title = new Font(Win95Style.CreateBoldFont().FontFamily, 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, "Mail Transfer Utility 95", title,
                new Rectangle(x, y, 300, 20), Win95Style.WindowText, TextFormatFlags.NoPadding);

            Font f = Win95Style.CreateFontSafe();
            Font b = Win95Style.BoldFont;
            y += 26;
            Line(g, f, x, ref y, "IMAP and Microsoft 365 mailbox migration utility");
            y += 6;
            Line(g, b, x, ref y, "Version 1.0");
            Line(g, f, x, ref y, "Developer:  Khaled Nabo");
            Line(g, f, x, ref y, "Company:  geissler-IT");
            y += 8;
            Line(g, f, 24, ref y, "Experimental software.");
            Line(g, f, 24, ref y, "Built for modern Windows with a classic interface.");
            y += 6;
            ClassicLabelLine(g, 24, ref y,
                "Uses imapsync (imapsync.lamiral.info) as the transfer backend - a separate");
            ClassicLabelLine(g, 24, ref y,
                "third-party project, not affiliated with this utility.");
        }

        private static void Line(Graphics g, Font f, int x, ref int y, string text)
        {
            TextRenderer.DrawText(g, text, f, new Rectangle(x, y, 360, 15),
                Win95Style.WindowText, TextFormatFlags.NoPadding);
            y += 16;
        }

        private static void ClassicLabelLine(Graphics g, int x, ref int y, string text)
        {
            TextRenderer.DrawText(g, text, Win95Style.CreateFontSafe(), new Rectangle(x, y, 380, 15),
                Win95Style.FaceDark, TextFormatFlags.NoPadding);
            y += 15;
        }

        /// <summary>Two envelopes with a transfer arrow between them.</summary>
        private static void DrawLogo(Graphics g, int x, int y)
        {
            DrawEnvelope(g, x, y + 6, 26, 18);
            DrawEnvelope(g, x + 24, y + 2, 26, 18);

            // Transfer arrow sweeping from the first to the second.
            Color navy = Color.FromArgb(0x00, 0x00, 0x80);
            using (Pen p = new Pen(navy, 2))
            {
                g.DrawLine(p, x + 10, y + 34, x + 40, y + 34);
                g.DrawLine(p, x + 40, y + 34, x + 34, y + 30);
                g.DrawLine(p, x + 40, y + 34, x + 34, y + 38);
            }
        }

        private static void DrawEnvelope(Graphics g, int x, int y, int w, int h)
        {
            Win95Style.FillRect(g, Color.White, x, y, w, h);
            Win95Style.DrawEdgePairPublic(g, new Rectangle(x, y, w, h),
                Color.FromArgb(0x60, 0x60, 0x60), Color.Black);
            using (Pen p = new Pen(Color.FromArgb(0x00, 0x00, 0x80)))
            {
                g.DrawLine(p, x + 1, y + 1, x + w / 2, y + h / 2);
                g.DrawLine(p, x + w - 1, y + 1, x + w / 2, y + h / 2);
            }
        }
    }
}
