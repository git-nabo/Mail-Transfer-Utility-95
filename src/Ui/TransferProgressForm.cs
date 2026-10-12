using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// The classic Windows 95 "mailbox copy" dialog: a hand-drawn frame animation of an
    /// envelope travelling from the source computer to the destination mailbox, live
    /// counters parsed from imapsync, an optional raw-output Details pane, and a Cancel
    /// button that stops the transfer cleanly. No modern spinner is used; when the total
    /// is unknown the progress bar runs an indeterminate classic marquee instead of
    /// faking a percentage.
    /// </summary>
    public sealed class TransferProgressForm : Win95Form
    {
        private readonly TransferJob _job;
        private readonly ImapSyncService _service = new ImapSyncService();
        private readonly TransferProgressParser _parser = new TransferProgressParser();

        private readonly object _queueLock = new object();
        private readonly Queue<string> _pending = new Queue<string>();
        private readonly StringBuilder _raw = new StringBuilder();
        private string _previewCommand = string.Empty;

        private DateTime _startTime;
        private bool _finished;
        private bool _detailsShown;
        private ImapSyncRunResult _finalResult;

        private Timer _timer;
        private Panel _animPanel;
        private Panel _infoPanel;
        private TextBox _rawBox;
        private SunkenPanel _rawHost;
        private ClassicButton _detailsButton;
        private ClassicButton _cancelButton;

        private const int CollapsedHeight = 316;
        private const int ExpandedHeight = 520;

        public MigrationReport Report { get; private set; }

        /// <summary>Milliseconds since the run started, used to drive smooth animation.</summary>
        private double AnimMs
        {
            get { return _startTime == DateTime.MinValue ? 0 : (DateTime.Now - _startTime).TotalMilliseconds; }
        }

        public TransferProgressForm(TransferJob job)
        {
            _job = job;

            BuildChrome("Mail Transfer");
            Width = 520;
            Height = CollapsedHeight;
            StartPosition = FormStartPosition.CenterParent;

            Build();
        }

        private void Build()
        {
            _animPanel = new DoubleBufferedPanel();
            _animPanel.SetBounds(0, 0, Width, 86);
            _animPanel.Dock = DockStyle.Top;
            _animPanel.Height = 86;
            _animPanel.BackColor = Win95Style.Face;
            _animPanel.Paint += delegate (object s, PaintEventArgs e) { PaintAnimation(e.Graphics); };
            ClientSurface.Controls.Add(_animPanel);

            _infoPanel = new DoubleBufferedPanel();
            _infoPanel.Dock = DockStyle.Top;
            _infoPanel.Height = 150;
            _infoPanel.BackColor = Win95Style.Face;
            _infoPanel.Paint += delegate (object s, PaintEventArgs e) { PaintInfo(e.Graphics); };
            ClientSurface.Controls.Add(_infoPanel);

            // Details (raw output) area, hidden until requested.
            _rawHost = new SunkenPanel();
            _rawHost.Dock = DockStyle.Fill;
            _rawHost.Visible = false;
            ClientSurface.Controls.Add(_rawHost);

            _rawBox = new TextBox();
            _rawBox.Multiline = true;
            _rawBox.ReadOnly = true;
            _rawBox.ScrollBars = ScrollBars.Both;
            _rawBox.WordWrap = false;
            _rawBox.BorderStyle = BorderStyle.None;
            _rawBox.BackColor = Win95Style.FieldBack;
            _rawBox.Font = Win95Style.CreateMonospaceFont();
            _rawBox.Dock = DockStyle.Fill;
            _rawHost.Controls.Add(_rawBox);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 40;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            _detailsButton = new ClassicButton();
            _detailsButton.Text = "Details >>";
            _detailsButton.SetBounds(14, 8, 100, 23);
            _detailsButton.Click += delegate { ToggleDetails(); };
            bar.Controls.Add(_detailsButton);

            _cancelButton = new ClassicButton();
            _cancelButton.Text = "Cancel";
            _cancelButton.SetBounds(Width - 114, 8, 96, 23);
            _cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cancelButton.IsDefaultButton = true;
            _cancelButton.Click += delegate { OnCancelOrClose(); };
            bar.Controls.Add(_cancelButton);

            // Keep docked panels in the right stacking order (fill area between top and bottom).
            _rawHost.BringToFront();
            _infoPanel.BringToFront();
            _animPanel.BringToFront();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_finished) return;

            _startTime = DateTime.Now;

            _service.Started += delegate (object s, string preview)
            {
                lock (_queueLock) { _previewCommand = preview; }
            };
            _service.LineReceived += delegate (object s, LineEventArgs ev)
            {
                string line = Redaction.Scrub(ev.Line);
                lock (_queueLock) _pending.Enqueue(line);
            };
            _service.Completed += delegate (object s, ImapSyncRunResult result)
            {
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke(new MethodInvoker(delegate { Finalize(result); }));
                }
                catch { }
            };

            _timer = new Timer();
            _timer.Interval = 40;   // ~25 fps for a smooth, flicker-free conveyor
            _timer.Tick += delegate { OnTick(); };
            _timer.Start();

            _service.Start(_job);
        }

        private void OnTick()
        {
            // Drain queued output lines.
            List<string> lines = null;
            lock (_queueLock)
            {
                if (_pending.Count > 0)
                {
                    lines = new List<string>(_pending.Count);
                    while (_pending.Count > 0) lines.Add(_pending.Dequeue());
                }
            }

            if (lines != null)
            {
                foreach (string line in lines)
                {
                    _parser.Apply(line);
                    _raw.Append(line).Append("\r\n");
                }
                TrimRaw();
                if (_detailsShown)
                    RefreshRawBox();
            }

            _animPanel.Invalidate();
            _infoPanel.Invalidate();
        }

        private void TrimRaw()
        {
            const int cap = 400000;
            if (_raw.Length > cap)
                _raw.Remove(0, _raw.Length - cap);
        }

        private void RefreshRawBox()
        {
            string header = string.IsNullOrEmpty(_previewCommand)
                ? string.Empty
                : _previewCommand + "\r\n" + new string('-', 60) + "\r\n";
            string text = header + _raw.ToString();
            if (_rawBox.TextLength != text.Length)
            {
                _rawBox.Text = text;
                _rawBox.SelectionStart = _rawBox.TextLength;
                _rawBox.ScrollToCaret();
            }
        }

        private void ToggleDetails()
        {
            _detailsShown = !_detailsShown;
            _rawHost.Visible = _detailsShown;
            _detailsButton.Text = _detailsShown ? "Details <<" : "Details >>";
            Height = _detailsShown ? ExpandedHeight : CollapsedHeight;
            if (_detailsShown) RefreshRawBox();
        }

        private void OnCancelOrClose()
        {
            if (_finished)
            {
                Close();
                return;
            }

            bool stop = Win95MessageBox.Ask(this, "Cancel Transfer",
                "Stop the current mailbox transfer?",
                "The mailbox may be only partially synchronized. You can re-run the same " +
                "job later to continue where it left off.");
            if (!stop)
                return;

            _cancelButton.Enabled = false;
            _cancelButton.Text = "Stopping...";
            _service.Cancel();
        }

        private void Finalize(ImapSyncRunResult result)
        {
            if (_finished) return;
            _finished = true;
            _finalResult = result;

            if (_timer != null) { _timer.Stop(); }

            // Drain any last lines.
            OnTick();

            Report = ReportService.Build(_job, _parser.Current, result, DateTime.Now - _startTime);

            _cancelButton.Enabled = true;
            _cancelButton.Text = "Close";
            _infoPanel.Invalidate();
            _animPanel.Invalidate();

            if (result != null && !string.IsNullOrEmpty(result.ErrorMessage) && !result.Started)
            {
                Win95MessageBox.ShowError(this, "Mail Transfer",
                    "The transfer could not be started.", result.ErrorMessage);
                return;
            }

            using (ReportForm rf = new ReportForm(Report))
                rf.ShowDialog(this);
        }

        protected override void RequestClose()
        {
            if (!_finished && _service.IsRunning)
            {
                OnCancelOrClose();
                return;
            }
            base.RequestClose();
        }

        // --- Painting -------------------------------------------------------------

        private void PaintAnimation(Graphics g)
        {
            Win95Style.PrepareGraphics(g);
            int w = _animPanel.Width, h = _animPanel.Height;
            Win95Style.FillRect(g, Win95Style.Face, 0, 0, w, h);

            // Left "old computer" and right "mailbox".
            int cy = 20;
            DrawComputer(g, 36, cy);
            DrawMailbox(g, w - 80, cy);

            // Dotted travel line between them.
            int lineY = cy + 20;
            for (int x = 92; x < w - 84; x += 6)
                Win95Style.FillRect(g, Win95Style.FaceDark, x, lineY, 2, 2);

            if (!_finished)
            {
                // Three envelopes gliding left -> right on a continuous conveyor. The
                // position is derived from elapsed time (not the tick count) so the motion
                // stays smooth and even, and the panel is double-buffered so it never
                // flickers.
                int startX = 96;
                int endX = w - 92;
                int span = endX - startX;
                if (span < 40) span = 40;

                int moved = (int)(AnimMs / 18.0);   // ~1px every 18ms
                for (int i = 0; i < 3; i++)
                {
                    int phase = (moved + i * (span / 3)) % span;
                    int ex = startX + phase;
                    DrawEnvelope(g, ex, lineY - 6);
                }
            }
            else
            {
                DrawEnvelope(g, (_animPanel.Width - 84), lineY - 6);
            }

            string title = _job.Options != null && _job.Options.DryRun
                ? "SIMULATION MODE - no changes are made"
                : (_finished ? (Report != null ? Report.HeadlineText : "Done.") : "Synchronizing mailbox...");

            Color tc = (_job.Options != null && _job.Options.DryRun) ? Win95Style.StatusFail : Win95Style.WindowText;
            TextRenderer.DrawText(g, title, Win95Style.BoldFont, new Rectangle(12, h - 18, w - 24, 16),
                tc, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter);
        }

        private static void DrawComputer(Graphics g, int x, int y)
        {
            // Beige monitor with a blue screen.
            Win95Style.FillRect(g, Color.FromArgb(0xD8, 0xD0, 0xB8), x, y, 36, 28);
            Win95Style.DrawRaisedBorder(g, new Rectangle(x, y, 36, 28), 2);
            Win95Style.FillRect(g, Color.FromArgb(0x00, 0x00, 0x80), x + 4, y + 4, 28, 18);
            Win95Style.DrawSunkenBorder(g, new Rectangle(x + 4, y + 4, 28, 18), 1);
            // Stand + base.
            Win95Style.FillRect(g, Color.FromArgb(0xC0, 0xB8, 0xA0), x + 14, y + 28, 8, 4);
            Win95Style.FillRect(g, Color.FromArgb(0xC0, 0xB8, 0xA0), x + 6, y + 32, 24, 4);
            Win95Style.DrawRaisedBorder(g, new Rectangle(x + 6, y + 32, 24, 4), 1);
        }

        private static void DrawMailbox(Graphics g, int x, int y)
        {
            // Classic post box.
            Win95Style.FillRect(g, Color.FromArgb(0x00, 0x60, 0x00), x + 4, y + 4, 28, 20);
            Win95Style.DrawRaisedBorder(g, new Rectangle(x + 4, y + 4, 28, 20), 2);
            // Rounded top band.
            Win95Style.FillRect(g, Color.FromArgb(0x00, 0x80, 0x00), x + 4, y + 4, 28, 6);
            // Red flag.
            Win95Style.FillRect(g, Color.FromArgb(0xC0, 0x00, 0x00), x + 30, y + 2, 6, 6);
            Win95Style.FillRect(g, Color.FromArgb(0x40, 0x40, 0x40), x + 30, y + 2, 2, 16);
            // Post.
            Win95Style.FillRect(g, Color.FromArgb(0x80, 0x60, 0x30), x + 16, y + 24, 4, 10);
        }

        private static void DrawEnvelope(Graphics g, int x, int y)
        {
            Win95Style.FillRect(g, Color.White, x, y, 14, 10);
            Win95Style.DrawEdgePairPublic(g, new Rectangle(x, y, 14, 10),
                Color.FromArgb(0x60, 0x60, 0x60), Color.Black);
            Win95Style.DrawLine(g, Color.FromArgb(0x00, 0x00, 0x80), x + 1, y + 1, x + 7, y + 6);
            Win95Style.DrawLine(g, Color.FromArgb(0x00, 0x00, 0x80), x + 12, y + 1, x + 6, y + 6);
        }

        private void PaintInfo(Graphics g)
        {
            Win95Style.PrepareGraphics(g);
            int w = _infoPanel.Width;
            Win95Style.FillRect(g, Win95Style.Face, 0, 0, w, _infoPanel.Height);

            TransferProgress p = _parser.Current;
            Font f = Win95Style.CreateFontSafe();
            Font b = Win95Style.BoldFont;

            int lx = 16, vx = 110;
            int y = 6;

            string src = EndpointText(_job.Source);
            string dst = EndpointText(_job.Destination);

            Row(g, b, f, lx, vx, ref y, "Source:", src);
            Row(g, b, f, lx, vx, ref y, "Destination:", dst);
            Row(g, b, f, lx, vx, ref y, "Current folder:", string.IsNullOrEmpty(p.CurrentFolder) ? "-" : p.CurrentFolder);

            string msgs = p.HasTotals
                ? p.CopiedMessages + " / " + p.TotalMessages
                : p.CopiedMessages.ToString();
            if (p.SkippedMessages > 0) msgs += "   (skipped " + p.SkippedMessages + ")";
            Row(g, b, f, lx, vx, ref y, "Messages:", msgs);

            string data = p.BytesTransferred > 0 ? MigrationReport.FormatBytes(p.BytesTransferred) : "-";
            TimeSpan elapsed = (_finished ? (Report != null ? Report.Duration : TimeSpan.Zero) : DateTime.Now - _startTime);
            double secs = Math.Max(1, elapsed.TotalSeconds);
            string speed = p.BytesTransferred > 0
                ? MigrationReport.FormatBytes((long)(p.BytesTransferred / secs)) + "/s"
                : "-";

            Row(g, b, f, lx, vx, ref y, "Data:", data);
            Row(g, b, f, lx, vx, ref y, "Elapsed:", MigrationReport.FormatDuration(elapsed) + "     Speed: " + speed);
            Row(g, b, f, lx, vx, ref y, "Status:", _finished
                ? (Report != null ? Report.HeadlineText : "Done")
                : (string.IsNullOrEmpty(p.Stage) ? "Working..." : p.Stage));

            // Progress bar.
            int barY = y + 6;
            Rectangle bar = new Rectangle(16, barY, w - 32, 18);
            DrawProgressBar(g, bar, p);
        }

        private static string EndpointText(TransferEndpoint ep)
        {
            if (ep == null) return "-";
            string u = ep.Auth == AuthMode.OAuth2 ? (ep.Account ?? ep.Username) : ep.Username;
            return (string.IsNullOrEmpty(u) ? "" : u + "  ") + "(" + ep.Host + ")";
        }

        private static void Row(Graphics g, Font b, Font f, int lx, int vx, ref int y, string label, string value)
        {
            TextRenderer.DrawText(g, label, b, new Rectangle(lx, y, vx - lx - 4, 15),
                Win95Style.WindowText, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value ?? "-", f, new Rectangle(vx, y, 390, 15),
                Win95Style.WindowText, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            y += 16;
        }

        private void DrawProgressBar(Graphics g, Rectangle bar, TransferProgress p)
        {
            Win95Style.FillRect(g, Win95Style.FieldBack, bar.X, bar.Y, bar.Width, bar.Height);
            Win95Style.DrawSunkenBorder(g, bar, 2);

            Rectangle inner = new Rectangle(bar.X + 3, bar.Y + 3, bar.Width - 6, bar.Height - 6);
            int block = 8, gap = 2;

            if (_finished)
            {
                FillBlocks(g, inner, 0, inner.Width, block, gap);
                return;
            }

            if (p.HasTotals && p.TotalMessages > 0)
            {
                double frac = Math.Min(1.0, (double)p.CopiedMessages / p.TotalMessages);
                FillBlocks(g, inner, 0, (int)(inner.Width * frac), block, gap);
            }
            else
            {
                // Indeterminate classic marquee: a run of blocks sweeping across, driven
                // by elapsed time so it glides smoothly.
                int runWidth = (block + gap) * 4;
                int travel = inner.Width + runWidth;
                int pos = (int)(AnimMs / 12.0) % travel - runWidth;
                int start = Math.Max(0, pos);
                int end = Math.Min(inner.Width, pos + runWidth);
                if (end > start)
                    FillBlocks(g, inner, start, end - start, block, gap);
            }
        }

        private static void FillBlocks(Graphics g, Rectangle inner, int offset, int width, int block, int gap)
        {
            int x = inner.X + offset;
            int limit = inner.X + offset + width;
            while (x + block <= limit && x + block <= inner.Right)
            {
                Win95Style.FillRect(g, Win95Style.SelectionBack, x, inner.Y, block, inner.Height);
                x += block + gap;
            }
        }
    }
}
