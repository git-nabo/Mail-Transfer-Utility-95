using System;
using System.Drawing;
using System.Windows.Forms;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Owner-drawn group box with the classic etched frame and legend. The legend is
    /// painted over the frame with a gap, exactly like the Windows 95 original.
    /// </summary>
    public class ClassicGroupBox : Control
    {
        public ClassicGroupBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.Face;
            ForeColor = Win95Style.WindowText;
            Height = 100;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            Rectangle frame = new Rectangle(1, 8, Width - 2, Height - 9);
            Win95Style.DrawEtchedFrame(g, frame);

            if (!string.IsNullOrEmpty(Text))
            {
                Size textSize = TextRenderer.MeasureText(g, Text, Font);
                int textX = 8;
                int textY = 1;

                Win95Style.FillRect(g, Win95Style.Face, textX - 3, textY, textSize.Width + 6, textSize.Height + 2);

                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(textX, textY, textSize.Width, textSize.Height),
                    ForeColor, TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>
    /// A Panel that paints without flicker. Used for the animated transfer strip and the
    /// live counter area so repainting does not clear-then-draw (which looked like the
    /// contents were disappearing and reappearing).
    /// </summary>
    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Win95Style.Face;
        }
    }

    /// <summary>Flat, square label using the classic system font.</summary>
    public class ClassicLabel : Control
    {
        private ContentAlignment _align = ContentAlignment.TopLeft;

        /// <summary>When true the text wraps to multiple lines instead of being ellipsized.</summary>
        public bool WordWrap { get; set; }

        public ClassicLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Win95Style.CreateFont();
            ForeColor = Win95Style.WindowText;
            BackColor = Win95Style.Face;
            Height = 15;
        }

        /// <summary>Optional override colour used instead of ForeColor when set.</summary>
        public Color? TextColorOverride { get; set; }

        public ContentAlignment TextAlign
        {
            get { return _align; }
            set { _align = value; Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Win95Style.PrepareGraphics(e.Graphics);

            Win95Style.FillRect(e.Graphics, BackColor, 0, 0, Width, Height);

            Color c = Enabled
                ? (TextColorOverride ?? ForeColor)
                : Win95Style.ButtonTextDisabled;

            TextFormatFlags flags = TextFormatFlags.NoPadding;
            if (WordWrap)
                flags |= TextFormatFlags.WordBreak;
            else
                flags |= TextFormatFlags.EndEllipsis;

            if (!WordWrap && (_align == ContentAlignment.MiddleLeft || _align == ContentAlignment.MiddleCenter ||
                _align == ContentAlignment.MiddleRight))
                flags |= TextFormatFlags.VerticalCenter;
            if (_align == ContentAlignment.TopRight || _align == ContentAlignment.MiddleRight ||
                _align == ContentAlignment.BottomRight)
                flags |= TextFormatFlags.Right;
            if (_align == ContentAlignment.TopCenter || _align == ContentAlignment.MiddleCenter ||
                _align == ContentAlignment.BottomCenter)
                flags |= TextFormatFlags.HorizontalCenter;

            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(0, 0, Width, Height), c, flags);
        }
    }

    /// <summary>
    /// Owner-drawn Windows 95 sunken panel used to host child controls with the
    /// correct 2px recessed border.
    /// </summary>
    public class SunkenPanel : Panel
    {
        public SunkenPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Win95Style.FieldBack;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Win95Style.PrepareGraphics(e.Graphics);
            Win95Style.FillRect(e.Graphics, BackColor, 0, 0, Width, Height);
            Win95Style.DrawSunkenBorder(e.Graphics, new Rectangle(0, 0, Width, Height), 2);
        }
    }

    /// <summary>
    /// Owner-drawn Win95 check box: 13x13 sunken square with a chunky check mark
    /// when checked, plus the classic label. Reacts to a single click.
    /// </summary>
    public class ClassicCheckBox : Control
    {
        private bool _checked;

        public ClassicCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.Face;
            ForeColor = Win95Style.WindowText;
            Height = 16;
            TabStop = true;
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                Invalidate();

                EventHandler h = CheckedChanged;
                if (h != null)
                    h(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled)
                Checked = !_checked;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
                Focus();
        }

        /// <summary>
        /// A single left click must toggle the box exactly once. The toggle lives in
        /// OnClick only (raised by the base class), so this override must never raise
        /// Click itself - doing so produced two toggles and the old "needs a
        /// double-click" symptom.
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            Rectangle box = new Rectangle(0, 1, 13, 13);
            Win95Style.FillRect(g, Enabled ? Win95Style.FieldBack : Win95Style.Face,
                                box.X, box.Y, box.Width, box.Height);
            Win95Style.DrawSunkenBorder(g, box, 2);

            if (_checked)
            {
                Color mark = Enabled ? Win95Style.WindowText : Win95Style.ButtonTextDisabled;
                using (Pen p = new Pen(mark, 2))
                {
                    g.DrawLine(p, 3, 7, 5, 10);
                    g.DrawLine(p, 5, 10, 10, 3);
                }
            }

            Color c = Enabled ? ForeColor : Win95Style.ButtonTextDisabled;
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(18, 1, Width - 18, Height),
                c, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

            if (Focused && Enabled)
                Win95Style.DrawFocusRect(g, new Rectangle(16,
                    1, Math.Min(Width - 16, TextRenderer.MeasureText(Text, Font).Width + 6), 14));
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && Enabled)
            {
                Checked = !_checked;
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Owner-drawn Win95 radio button: a 12px sunken circle with a filled dot when
    /// selected. Radios that share a <see cref="GroupName"/> within the same parent are
    /// mutually exclusive. Reacts to a single click.
    /// </summary>
    public class ClassicRadioButton : Control
    {
        private bool _checked;

        public ClassicRadioButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.Face;
            ForeColor = Win95Style.WindowText;
            Height = 16;
            TabStop = true;
        }

        public event EventHandler CheckedChanged;

        /// <summary>Radios with the same non-null group name are mutually exclusive.</summary>
        public string GroupName { get; set; }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                if (_checked)
                    UncheckGroupSiblings();

                Invalidate();

                EventHandler h = CheckedChanged;
                if (h != null)
                    h(this, EventArgs.Empty);
            }
        }

        private void UncheckGroupSiblings()
        {
            if (Parent == null)
                return;

            foreach (Control c in Parent.Controls)
            {
                ClassicRadioButton r = c as ClassicRadioButton;
                if (r != null && !ReferenceEquals(r, this) &&
                    string.Equals(r.GroupName, GroupName, StringComparison.Ordinal))
                {
                    r.Checked = false;
                }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled && !_checked)
                Checked = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
                Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && Enabled && !_checked)
            {
                Checked = true;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            // Classic 12x12 radio: a sunken white well (dark upper-left arc, light
            // lower-right arc). The two arcs are complementary semicircles, so the ring
            // is closed with no notch.
            int top = 2;
            Rectangle circle = new Rectangle(0, top, 11, 11);
            Rectangle inner = new Rectangle(circle.X + 1, circle.Y + 1, circle.Width - 2, circle.Height - 2);

            using (SolidBrush well = new SolidBrush(Enabled ? Win95Style.FieldBack : Win95Style.Face))
                g.FillEllipse(well, circle);

            using (Pen black = new Pen(Win95Style.FaceShadow))
            using (Pen dark = new Pen(Win95Style.FaceDark))
            using (Pen white = new Pen(Color.White))
            using (Pen face = new Pen(Win95Style.FaceLight))
            {
                // Upper-left (shadow) semicircle and lower-right (highlight) semicircle.
                g.DrawArc(black, circle, 135, 180);
                g.DrawArc(dark, inner, 135, 180);
                g.DrawArc(white, circle, 315, 180);
                g.DrawArc(face, inner, 315, 180);
            }

            if (_checked)
            {
                Color dot = Enabled ? Win95Style.WindowText : Win95Style.ButtonTextDisabled;
                Win95Style.FillRect(g, dot, 4, top + 4, 4, 4);
                Win95Style.FillRect(g, dot, 5, top + 3, 2, 6);
                Win95Style.FillRect(g, dot, 3, top + 5, 6, 2);
            }

            Color c = Enabled ? ForeColor : Win95Style.ButtonTextDisabled;
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(17, 1, Width - 17, Height),
                c, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

            if (Focused && Enabled)
                Win95Style.DrawFocusRect(g, new Rectangle(15, 1,
                    Math.Min(Width - 15, TextRenderer.MeasureText(Text, Font).Width + 6), 14));
        }
    }
}
