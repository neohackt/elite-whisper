using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EliteWhisper.Services
{
    public class HotkeyService
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const int WM_HOTKEY = 0x0312;

        public event Action? OnHotkeyPressed;
        private HwndSource? _source;

        public void Register(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("HotkeyService: Cannot register with zero handle");
                return;
            }
            
            _source = HwndSource.FromHwnd(windowHandle);
            if (_source == null)
            {
                System.Diagnostics.Debug.WriteLine("HotkeyService: Failed to get HwndSource");
                return;
            }
            
            _source.AddHook(HwndHook);

            // Register F2 (VK_F2 = 0x71 = 113)
            bool success = RegisterHotKey(windowHandle, HOTKEY_ID, 0, 0x71);
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine("Failed to register hotkey F2");
            }
        }

        public void Unregister(IntPtr windowHandle)
        {
            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
                _source = null;
            }
            UnregisterHotKey(windowHandle, HOTKEY_ID);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                if (wParam.ToInt32() == HOTKEY_ID)
                {
                    OnHotkeyPressed?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
    }
}
