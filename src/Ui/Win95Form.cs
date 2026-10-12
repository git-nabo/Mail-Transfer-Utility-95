using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MailTransfer95.Ui
{
    /// <summary>
    /// Base form that reproduces the Windows 95 window frame: a 3D raised outer
    /// border, a navy caption bar and an optional menu bar. The standard Aero/Mica
    /// chrome is disabled so nothing modern leaks into the appearance.
    ///
    /// The window is borderless (FormBorderStyle.None) purely for looks, so the
    /// non-client behaviours that Windows normally provides - dragging by the
    /// caption, Alt+F4, taskbar integration - are supplied explicitly below.
    /// </summary>
    public class Win95Form : Form
    {
        protected internal Win95TitleBar TitleBarControl;
        protected internal Panel ClientSurface;

        private bool _allowMaximize;

        // The two native icon handles, kept alive for the lifetime of the form. The
        // taskbar and Alt+Tab read them straight from the window.
        private Icon _bigIcon;
        private Icon _smallIcon;
        private bool _appIconApplied;

        // Non-client hit testing constants, used to hand dragging back to Windows.
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;

        // Window icon constants. The taskbar and Alt+Tab read the icon from the window
        // itself, so it has to be pushed to the HWND and not only set on the Form.
        private const int WM_SETICON = 0x0080;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL = 0;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index,
                                                 IntPtr[] largeIcons, IntPtr[] smallIcons,
                                                 uint count);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        public Win95Form()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Win95Style.Face;
            Font = Win95Style.CreateFont();
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;

            // The custom caption bar hides the system icon on screen, but Windows
            // still needs the Form icon to show the right image in the taskbar,
            // on the Alt+Tab switcher and in the window caption.
            ApplyApplicationIcon();
        }

        /// <summary>
        /// Assigns the application icon to this window so the taskbar button, the
        /// Alt+Tab switcher and the window caption all show it. ExtractIconEx is used
        /// (not System.Drawing.Icon) because the PNG-compressed frames in app.ico
        /// decode correctly only through the Win32 loader. A failure here must never
        /// stop the window from opening.
        /// </summary>
        private void ApplyApplicationIcon()
        {
            try
            {
                if (_appIconApplied) return;
                _appIconApplied = true;

                string exe = Application.ExecutablePath;
                if (string.IsNullOrEmpty(exe)) return;

                if (TryLoadAppIcons(exe, out _bigIcon, out _smallIcon) == false &&
                    (_bigIcon == null && _smallIcon == null))
                {
                    Icon = Icon.ExtractAssociatedIcon(exe);
                }
                else
                {
                    Icon = _bigIcon ?? _smallIcon;
                }

                ShowIcon = true;
            }
            catch
            {
                // Keep the default icon if anything goes wrong.
            }
        }

        /// <summary>
        /// Loads the large (32x32) and small (16x16) icons out of an executable. Every
        /// icon group is walked and the widest image kept as the large icon, so an EXE
        /// whose first group is 16x16 still yields a correct large (Alt+Tab) icon.
        /// </summary>
        internal static bool TryLoadAppIcons(string exe, out Icon large, out Icon small)
        {
            large = null;
            small = null;

            IntPtr largeHandle = IntPtr.Zero;
            IntPtr smallHandle = IntPtr.Zero;

            uint groups = ExtractIconEx(exe, -1, null, null, 0);
            if (groups == 0) groups = 1;

            for (uint i = 0; i < groups; i++)
            {
                IntPtr[] groupLarge = new IntPtr[1];
                IntPtr[] groupSmall = new IntPtr[1];

                if (ExtractIconEx(exe, (int)i, groupLarge, groupSmall, 1) == 0)
                    continue;

                if (smallHandle == IntPtr.Zero && groupSmall[0] != IntPtr.Zero)
                    smallHandle = groupSmall[0];

                if (groupLarge[0] != IntPtr.Zero &&
                    (largeHandle == IntPtr.Zero || IconWidth(groupLarge[0]) > IconWidth(largeHandle)))
                {
                    largeHandle = groupLarge[0];
                }

                if (largeHandle != IntPtr.Zero && IconWidth(largeHandle) >= 32 &&
                    smallHandle != IntPtr.Zero)
                {
                    break;
                }
            }

            if (largeHandle != IntPtr.Zero) large = Icon.FromHandle(largeHandle);
            if (smallHandle != IntPtr.Zero) small = Icon.FromHandle(smallHandle);

            return large != null || small != null;
        }

        /// <summary>Width in pixels of an HICON, used to pick the large icon group.</summary>
        private static int IconWidth(IntPtr icon)
        {
            ICONINFO info;
            if (icon == IntPtr.Zero || !GetIconInfo(icon, out info) || info.hbmColor == IntPtr.Zero)
                return 0;

            BITMAP bitmap;
            return GetObject(info.hbmColor, Marshal.SizeOf(typeof(BITMAP)), out bitmap) == 0
                ? 0
                : bitmap.bmWidth;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public short bmPlanes;
            public short bmBitCount;
            public IntPtr bmBits;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetObject(IntPtr hObject, int nSize, out BITMAP lpObject);

        /// <summary>Pushes the icons into the real native window once the handle exists.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyNativeIcon();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyNativeIcon();
        }

        private void ApplyNativeIcon()
        {
            try
            {
                if (!IsHandleCreated) return;
                if (_bigIcon == null && _smallIcon == null && Icon == null) return;

                // BIG goes to Alt+Tab, SMALL to the taskbar button and the caption.
                if (_bigIcon != null)
                    SendMessage(Handle, WM_SETICON, (IntPtr)ICON_BIG, _bigIcon.Handle);

                if (_smallIcon != null)
                    SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, _smallIcon.Handle);
                else if (Icon != null)
                    SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, Icon.Handle);
            }
            catch
            {
                // Never let icon bookkeeping break the window.
            }
        }

        /// <summary>Allows the caption to show a maximise button.</summary>
        protected bool AllowMaximize
        {
            get { return _allowMaximize; }
            set
            {
                _allowMaximize = value;
                if (TitleBarControl != null)
                    TitleBarControl.MaximizeBox = value;
            }
        }

        /// <summary>
        /// Starts a native caption drag. Calling SendMessage(WM_NCLBUTTONDOWN,
        /// HTCAPTION) hands the move loop back to Windows, which gives correct
        /// cursor tracking, snapping and drag-to-edge behaviour - and works with
        /// just one click, no double-click required.
        /// </summary>
        public void BeginCaptionDrag()
        {
            try
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            catch
            {
                DragToCursor();
            }
        }

        private bool _manualDragging;
        private Point _manualDragOrigin;

        private void DragToCursor()
        {
            if (!_manualDragging)
            {
                _manualDragging = true;
                _manualDragOrigin = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
                return;
            }

            Point cursor = Cursor.Position;
            Location = new Point(cursor.X - _manualDragOrigin.X, cursor.Y - _manualDragOrigin.Y);
        }

        public void EndCaptionDrag()
        {
            _manualDragging = false;
        }

        public bool IsManualDragging
        {
            get { return _manualDragging; }
        }

        /// <summary>
        /// Builds the standard chrome and places the client surface inside it.
        /// Call at the end of the derived constructor, before adding controls.
        /// </summary>
        protected internal void BuildChrome(string caption)
        {
            Text = caption;

            // The fill surface must be added BEFORE the caption bar. WinForms reserves Dock
            // space in reverse add order, so adding the caption first would let the fill
            // panel claim the whole client area and hide both the caption and menu bar.
            Panel surface = new Panel();
            surface.Dock = DockStyle.Fill;
            surface.BackColor = Win95Style.Face;
            Controls.Add(surface);
            ClientSurface = surface;

            Win95TitleBar title = new Win95TitleBar();
            title.Text = caption;
            title.Dock = DockStyle.Top;
            title.Height = 20;
            title.MaximizeBox = _allowMaximize;
            title.CloseClicked += delegate { RequestClose(); };
            title.MinimizeClicked += delegate { WindowState = FormWindowState.Minimized; };
            title.MaximizeClicked += delegate { ToggleMaximize(); };
            title.SystemMenuRequested += delegate { ShowSystemMenu(); };
            Controls.Add(title);
            TitleBarControl = title;
        }

        /// <summary>Adds a menu bar styled to match the Win95 look.</summary>
        protected void BuildMenuBar(MenuStrip menu)
        {
            menu.Dock = DockStyle.Top;
            menu.Font = Win95Style.CreateFont();
            menu.BackColor = Win95Style.Face;
            menu.ForeColor = Win95Style.WindowText;
            // System render mode uses the OS theme's highlight colours for menu hover,
            // so selected items keep white-on-navy text instead of the broken
            // black-on-dark-blue that a custom professional renderer produced.
            menu.RenderMode = ToolStripRenderMode.System;
            menu.Padding = new Padding(2, 0, 0, 0);
            menu.Height = Win95Style.MenuHeight;

            ClientSurface.Controls.Add(menu);
        }

        protected virtual void RequestClose()
        {
            Close();
        }

        protected void ToggleMaximize()
        {
            if (!_allowMaximize)
                return;

            WindowState = (WindowState == FormWindowState.Maximized)
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void ShowSystemMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Win95Style.CreateFont();
            menu.RenderMode = ToolStripRenderMode.System;
            menu.Items.Add("Restore", null, delegate { WindowState = FormWindowState.Normal; });
            menu.Items.Add("Minimize", null, delegate { WindowState = FormWindowState.Minimized; });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close", null, delegate { RequestClose(); });

            menu.Show(this, new Point(4, TitleBarControl.Height + 4));
        }

        /// <summary>
        /// Escape closes the window, matching classic dialog behaviour.
        /// Dialogs opt in; the main window does not close on a stray Escape.
        /// </summary>
        public bool CloseOnEscape { get; set; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && CloseOnEscape)
            {
                RequestClose();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// With FormBorderStyle.None the window has no system frame, so the usual
        /// Windows affordances (Alt+F4, the taskbar Close command) are restored here.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0112)          // WM_SYSCOMMAND
            {
                int command = (int)m.WParam.ToInt64() & 0xFFF0;
                if (command == 0xF060)    // SC_CLOSE
                {
                    RequestClose();
                    return;
                }
            }

            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);
            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);
        }
    }
}
