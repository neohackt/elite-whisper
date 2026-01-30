using System;
using System.Runtime.InteropServices;

namespace EliteWhisper.Native
{
    public static class Win32
    {
        // Window Styles
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;    // Hide from Alt-Tab
        public const int WS_EX_NOACTIVATE = 0x08000000;    // Do not steal focus
        public const int WS_EX_TOPMOST = 0x00000008;       // Always on top
        public const int WS_EX_TRANSPARENT = 0x00000020;   // Click-through (optional, not using yet)

        // SetWindowPos Flags
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
