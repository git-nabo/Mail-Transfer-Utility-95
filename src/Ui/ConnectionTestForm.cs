using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MailTransfer95.Core;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Runs a connection test on a background thread and shows each verification step
    /// with a classic green [OK] / red [FAIL] marker. Testing never starts a transfer.
    /// </summary>
    public sealed class ConnectionTestForm : Win95Form
    {
        private readonly TransferEndpoint _endpoint;
        private readonly int _timeout;
        private ConnectionTestResult _result;
        private bool _running = true;

        private Panel _resultsPanel;
        private ClassicButton _closeButton;

        public ConnectionTestForm(TransferEndpoint endpoint, int timeoutSeconds)
        {
            _endpoint = endpoint;
            _timeout = timeoutSeconds <= 0 ? 60 : timeoutSeconds;

            BuildChrome("Test Connection");
            CloseOnEscape = true;
            Width = 460;
            Height = 250;
            StartPosition = FormStartPosition.CenterParent;

            _resultsPanel = new Panel();
            _resultsPanel.Dock = DockStyle.Fill;
            _resultsPanel.BackColor = Win95Style.Face;
            _resultsPanel.Paint += delegate (object s, PaintEventArgs e) { PaintResults(e.Graphics); };
            ClientSurface.Controls.Add(_resultsPanel);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 38;
            bar.BackColor = Win95Style.Face;
            ClientSurface.Controls.Add(bar);

            _closeButton = new ClassicButton();
            _closeButton.Text = "Close";
            _closeButton.SetBounds(Width - 110, 7, 90, 23);
            _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _closeButton.IsDefaultButton = true;
            _closeButton.Enabled = false;
            _closeButton.Click += delegate { Close(); };
            bar.Controls.Add(_closeButton);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Thread t = new Thread(RunTest) { IsBackground = true };
            t.Start();
        }

        private void RunTest()
        {
            ConnectionTestResult r = ConnectionTestService.Test(_endpoint, _timeout);
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    _result = r;
                    _running = false;
                    _closeButton.Enabled = true;
                    _closeButton.Focus();
                    SizeToContent();
                    _resultsPanel.Invalidate();
                }));
            }
            catch { /* form closed */ }
        }

        private void SizeToContent()
        {
            int rows = _result != null ? _result.Steps.Count : 5;
            int needed = 60 + rows * 24 + 54;
            if (needed > Height)
                Height = needed;
        }

        private void PaintResults(Graphics g)
        {
            Win95Style.PrepareGraphics(g);
            Win95Style.FillRect(g, Win95Style.Face, 0, 0, _resultsPanel.Width, _resultsPanel.Height);

            Font font = Win95Style.CreateFontSafe();
            Font bold = Win95Style.BoldFont;

            string target = (_endpoint.Auth == AuthMode.OAuth2 ? _endpoint.Account : _endpoint.Username) +
                            "  @  " + _endpoint.Host + ":" + _endpoint.Port;
            TextRenderer.DrawText(g, target, bold, new Rectangle(16, 12, _resultsPanel.Width - 32, 16),
                Win95Style.WindowText, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (_running || _result == null)
            {
                TextRenderer.DrawText(g, "Testing connection, please wait...", font,
                    new Rectangle(16, 44, _resultsPanel.Width - 32, 16),
                    Win95Style.WindowText, TextFormatFlags.NoPadding);
                return;
            }

            int y = 42;
            foreach (TestStep step in _result.Steps)
            {
                string marker = step.Ok ? "[OK]" : "[FAIL]";
                Color c = step.Ok ? Win95Style.StatusOk : Win95Style.StatusFail;

                TextRenderer.DrawText(g, marker, bold, new Rectangle(16, y, 48, 16), c,
                    TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, step.Name, bold, new Rectangle(66, y, 150, 16),
                    Win95Style.WindowText, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, step.Detail ?? string.Empty, font,
                    new Rectangle(220, y, _resultsPanel.Width - 236, 16),
                    Win95Style.FaceDark, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += 24;
            }

            y += 6;
            string summary = _result.Success ? "All checks passed." : "One or more checks failed.";
            TextRenderer.DrawText(g, summary, bold, new Rectangle(16, y, _resultsPanel.Width - 32, 16),
                _result.Success ? Win95Style.StatusOk : Win95Style.StatusFail,
                TextFormatFlags.NoPadding);
        }
    }
}
