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
        private IntPtr _currentWindowHandle;

        // Default to F2 (VK_F2 = 0x71)
        public uint CurrentVirtualKey { get; set; } = 0x71;
        public uint CurrentModifiers { get; set; } = 0;

        public void Register(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("HotkeyService: Cannot register with zero handle");
                return;
            }
            
            _currentWindowHandle = windowHandle;
            _source = HwndSource.FromHwnd(windowHandle);
            if (_source == null)
            {
                System.Diagnostics.Debug.WriteLine("HotkeyService: Failed to get HwndSource");
                return;
            }
            
            _source.AddHook(HwndHook);

            // Register with current key settings
            bool success = RegisterHotKey(windowHandle, HOTKEY_ID, CurrentModifiers, CurrentVirtualKey);
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register hotkey VK={CurrentVirtualKey:X} Modifiers={CurrentModifiers}");
            }
        }

        public bool UpdateHotkey(uint vk, uint modifiers)
        {
            CurrentVirtualKey = vk;
            CurrentModifiers = modifiers;

            if (_currentWindowHandle != IntPtr.Zero)
            {
                // Unregister old hotkey
                UnregisterHotKey(_currentWindowHandle, HOTKEY_ID);
                
                // Register new hotkey
                bool success = RegisterHotKey(_currentWindowHandle, HOTKEY_ID, CurrentModifiers, CurrentVirtualKey);
                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update hotkey to VK={CurrentVirtualKey:X} Modifiers={CurrentModifiers}");
                    return false;
                }
                return true;
            }
            return false;
        }

        public void Unregister(IntPtr windowHandle)
        {
            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
                _source = null;
            }
            UnregisterHotKey(windowHandle, HOTKEY_ID);
            _currentWindowHandle = IntPtr.Zero;
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
