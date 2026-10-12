using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MailTransfer95.Ui
{
    /// <summary>One row in a <see cref="ClassicListView"/>.</summary>
    public sealed class ListRow
    {
        public string[] Cells;
        public bool Checkable;
        public bool Checked;
        public object Tag;
        public Color? ForeOverride;

        public ListRow(params string[] cells)
        {
            Cells = cells ?? new string[0];
        }
    }

    /// <summary>A header column: a caption and a fixed pixel width.</summary>
    public sealed class ListColumn
    {
        public string Caption;
        public int Width;

        public ListColumn(string caption, int width)
        {
            Caption = caption;
            Width = width;
        }
    }

    /// <summary>
    /// Owner-drawn Windows 95 report-style list: white sunken field, optional column
    /// header, optional per-row check boxes, navy selection bar and a classic 3D
    /// scrollbar. Single-select. Used for the folder picker and the saved-jobs table.
    /// </summary>
    public class ClassicListView : Control
    {
        private readonly List<ListColumn> _columns = new List<ListColumn>();
        private readonly List<ListRow> _rows = new List<ListRow>();
        private int _selectedIndex = -1;
        private int _scrollIndex;
        private int _hoverIndex = -1;
        private bool _hasFocus;

        private const int RowHeight = 18;
        private const int HeaderHeight = 18;
        private const int ScrollBarWidth = 16;
        private const int CheckWidth = 20;

        public event EventHandler SelectedIndexChanged;
        public event EventHandler ItemChecked;
        public event EventHandler ItemActivate;

        public bool ShowCheckBoxes { get; set; }
        public bool ShowHeader { get; set; }

        public ClassicListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.FieldBack;
            TabStop = true;
        }

        public IList<ListColumn> Columns { get { return _columns; } }
        public IList<ListRow> Rows { get { return _rows; } }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                if (value < -1 || value >= _rows.Count) value = -1;
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                EnsureVisible();
                Invalidate();
                Raise(SelectedIndexChanged);
            }
        }

        public ListRow SelectedRow
        {
            get { return (_selectedIndex >= 0 && _selectedIndex < _rows.Count) ? _rows[_selectedIndex] : null; }
        }

        public void SetRows(IEnumerable<ListRow> rows)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);
            _selectedIndex = -1;
            _scrollIndex = 0;
            _hoverIndex = -1;
            Invalidate();
        }

        public void ClearRows()
        {
            _rows.Clear();
            _selectedIndex = -1;
            _scrollIndex = 0;
            Invalidate();
        }

        private int TopOffset { get { return (ShowHeader ? HeaderHeight : 0) + 2; } }

        private int VisibleRowCount
        {
            get { return Math.Max(1, (Height - TopOffset - 2) / RowHeight); }
        }

        public bool NeedsScrollBar { get { return _rows.Count > VisibleRowCount; } }

        private void EnsureVisible()
        {
            if (_selectedIndex < 0) return;
            if (_selectedIndex < _scrollIndex) _scrollIndex = _selectedIndex;
            else if (_selectedIndex >= _scrollIndex + VisibleRowCount)
                _scrollIndex = _selectedIndex - VisibleRowCount + 1;
        }

        private int IndexFromPoint(int y)
        {
            int index = _scrollIndex + ((y - TopOffset) / RowHeight);
            if (index < 0 || index >= _rows.Count) return -1;
            return index;
        }

        private void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexFromPoint(e.Y);
            if (index != _hoverIndex) { _hoverIndex = index; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex != -1) { _hoverIndex = -1; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * 3);
        }

        public void ScrollBy(int delta)
        {
            int maxScroll = Math.Max(0, _rows.Count - VisibleRowCount);
            int newScroll = _scrollIndex + delta;
            if (newScroll < 0) newScroll = 0;
            if (newScroll > maxScroll) newScroll = maxScroll;
            if (newScroll != _scrollIndex) { _scrollIndex = newScroll; Invalidate(); }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); _hasFocus = true; Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _hasFocus = false; Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp:
                case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Space:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: if (_selectedIndex > 0) SelectedIndex = _selectedIndex - 1; break;
                case Keys.Down: if (_selectedIndex < _rows.Count - 1) SelectedIndex = _selectedIndex + 1; break;
                case Keys.Home: if (_rows.Count > 0) SelectedIndex = 0; break;
                case Keys.End: if (_rows.Count > 0) SelectedIndex = _rows.Count - 1; break;
                case Keys.Space: ToggleCheck(_selectedIndex); break;
            }
        }

        private void ToggleCheck(int index)
        {
            if (!ShowCheckBoxes || index < 0 || index >= _rows.Count) return;
            if (!_rows[index].Checkable) return;
            _rows[index].Checked = !_rows[index].Checked;
            Invalidate();
            Raise(ItemChecked);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (NeedsScrollBar)
            {
                Rectangle sb = new Rectangle(Width - ScrollBarWidth - 1, 1, ScrollBarWidth, Height - 2);
                if (sb.Contains(e.Location))
                {
                    if (e.Y <= sb.Y + ScrollBarWidth) ScrollBy(-1);
                    else if (e.Y >= sb.Bottom - ScrollBarWidth) ScrollBy(1);
                    else ScrollBy(e.Y < sb.Y + sb.Height / 2 ? -VisibleRowCount : VisibleRowCount);
                    return;
                }
            }

            int index = IndexFromPoint(e.Y);
            if (index < 0) return;

            if (ShowCheckBoxes && _rows[index].Checkable && e.X < TopOffsetLeft() + CheckWidth)
            {
                SelectedIndex = index;
                ToggleCheck(index);
                return;
            }

            SelectedIndex = index;
        }

        private int TopOffsetLeft() { return 2; }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int index = IndexFromPoint(e.Y);
            if (index >= 0) { SelectedIndex = index; Raise(ItemActivate); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            int sbWidth = NeedsScrollBar ? ScrollBarWidth : 0;
            int listRight = Width - sbWidth - 2;

            Win95Style.FillRect(g, Win95Style.FieldBack, 0, 0, Width, Height);
            Win95Style.DrawSunkenBorder(g, new Rectangle(0, 0, Width, Height), 2);

            if (ShowHeader)
                DrawHeader(g, listRight);

            System.Drawing.Region saved = g.Clip;
            g.SetClip(new Rectangle(2, TopOffset, listRight - 2, Height - TopOffset - 2));

            int visible = VisibleRowCount;
            for (int i = 0; i < visible; i++)
            {
                int index = _scrollIndex + i;
                if (index >= _rows.Count) break;

                ListRow row = _rows[index];
                int y = TopOffset + (i * RowHeight);
                Rectangle rr = new Rectangle(2, y, listRight - 2, RowHeight);

                bool isSelected = (index == _selectedIndex);
                if (isSelected)
                    Win95Style.FillRect(g, _hasFocus ? Win95Style.SelectionBack : Win95Style.SelectionInactiveBack,
                                        rr.Left, rr.Top, rr.Width, rr.Height);
                else if (index == _hoverIndex)
                    Win95Style.FillRect(g, Win95Style.FaceLight, rr.Left, rr.Top, rr.Width, rr.Height);

                Color textColor = isSelected
                    ? (_hasFocus ? Win95Style.SelectionText : Win95Style.WindowText)
                    : (row.ForeOverride ?? Win95Style.FieldText);

                int cx = rr.Left + 3;

                if (ShowCheckBoxes)
                {
                    if (row.Checkable)
                        DrawCheck(g, rr.Left + 3, rr.Top + 2, row.Checked);
                    cx = rr.Left + CheckWidth + 2;
                }

                for (int ci = 0; ci < row.Cells.Length; ci++)
                {
                    int colWidth = (ci < _columns.Count) ? _columns[ci].Width : (listRight - cx - 4);
                    TextRenderer.DrawText(g, row.Cells[ci], Font,
                        new Rectangle(cx, rr.Top, colWidth - 4, RowHeight),
                        textColor, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                                   TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    cx += colWidth;
                }
            }

            g.Clip = saved;

            if (NeedsScrollBar)
                DrawScrollBar(g, new Rectangle(Width - ScrollBarWidth - 1, 1, ScrollBarWidth, Height - 2));

            if (_hasFocus)
                Win95Style.DrawFocusRect(g, new Rectangle(1, 1, Width - 2, Height - 2));
        }

        private void DrawHeader(Graphics g, int listRight)
        {
            Rectangle header = new Rectangle(2, 2, listRight - 2, HeaderHeight);
            int x = header.X;
            if (ShowCheckBoxes) x += CheckWidth;

            for (int i = 0; i < _columns.Count; i++)
            {
                Rectangle cell = new Rectangle(x, header.Y, _columns[i].Width, header.Height);
                Win95Style.FillRect(g, Win95Style.Face, cell.X, cell.Y, cell.Width, cell.Height);
                Win95Style.DrawRaisedBorder(g, cell, 2);
                TextRenderer.DrawText(g, _columns[i].Caption, Font,
                    new Rectangle(cell.X + 4, cell.Y, cell.Width - 6, cell.Height),
                    Win95Style.WindowText, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                                           TextFormatFlags.EndEllipsis);
                x += _columns[i].Width;
            }
        }

        private static void DrawCheck(Graphics g, int x, int y, bool on)
        {
            Rectangle box = new Rectangle(x, y, 13, 13);
            Win95Style.FillRect(g, Win95Style.FieldBack, box.X, box.Y, box.Width, box.Height);
            Win95Style.DrawSunkenBorder(g, box, 2);
            if (on)
            {
                using (Pen p = new Pen(Win95Style.WindowText, 2))
                {
                    g.DrawLine(p, x + 3, y + 7, x + 5, y + 10);
                    g.DrawLine(p, x + 5, y + 10, x + 10, y + 3);
                }
            }
        }

        private void DrawScrollBar(Graphics g, Rectangle r)
        {
            Win95Style.FillRect(g, Win95Style.Face, r.X, r.Y, r.Width, r.Height);
            Win95Style.DrawEdgePairPublic(g, r, Win95Style.FaceLight, Win95Style.FaceShadow);

            int arrowSize = ScrollBarWidth;
            Rectangle upButton = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, arrowSize);
            DrawArrowButton(g, upButton, _scrollIndex > 0, true);

            Rectangle downButton = new Rectangle(r.X + 1, r.Bottom - arrowSize - 1, r.Width - 2, arrowSize);
            DrawArrowButton(g, downButton, _scrollIndex < _rows.Count - VisibleRowCount, false);

            int trackTop = upButton.Bottom;
            int trackHeight = downButton.Top - trackTop;
            if (trackHeight <= 0) return;

            Win95Style.FillDither(g, new Rectangle(r.X + 1, trackTop, r.Width - 2, trackHeight),
                                  Win95Style.FaceLight, Win95Style.Face);

            int pageSize = VisibleRowCount;
            int thumbHeight = Math.Max(8, (int)((double)trackHeight * pageSize / Math.Max(pageSize, _rows.Count)));
            int maxScroll = Math.Max(1, _rows.Count - pageSize);
            double fraction = (double)_scrollIndex / maxScroll;
            if (fraction > 1.0) fraction = 1.0;

            int thumbY = trackTop + (int)(fraction * (trackHeight - thumbHeight));
            Rectangle thumb = new Rectangle(r.X + 1, thumbY, r.Width - 2, thumbHeight);
            Win95Style.FillRect(g, Win95Style.Face, thumb.X, thumb.Y, thumb.Width, thumb.Height);
            Win95Style.DrawRaisedBorder(g, thumb, 2);
        }

        private static void DrawArrowButton(Graphics g, Rectangle r, bool enabled, bool up)
        {
            Win95Style.FillRect(g, Win95Style.Face, r.X, r.Y, r.Width, r.Height);
            Win95Style.DrawRaisedBorder(g, r, 2);

            Color c = enabled ? Win95Style.WindowText : Win95Style.ButtonTextDisabled;
            int cx = r.X + (r.Width / 2);
            int cy = r.Y + (r.Height / 2);

            for (int i = 0; i < 4; i++)
            {
                int halfWidth = 4 - i;
                int y = up ? cy + 2 - i : cy - 3 + i;
                Win95Style.DrawLine(g, c, cx - halfWidth, y, cx + halfWidth, y);
            }
        }
    }
}
