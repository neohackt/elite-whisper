using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace EliteWhisper.Services
{
    public class TextInjectionService
    {
        #region Win32 Imports

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            internal uint type;
            internal InputUnion U;
            internal static int Size => Marshal.SizeOf(typeof(INPUT));
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] internal MOUSEINPUT mi;
            [FieldOffset(0)] internal KEYBDINPUT ki;
            [FieldOffset(0)] internal HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            internal int dx, dy;
            internal uint mouseData, dwFlags, time;
            internal UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            internal ushort wVk;
            internal ushort wScan;
            internal uint dwFlags;
            internal uint time;
            internal UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            internal uint uMsg;
            internal ushort wParamL, wParamH;
        }

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        // Virtual Key Codes for clipboard paste
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;

        #endregion

        // Configuration
        public int TypingDelayMs { get; set; } = 0; // 0 = instant, >0 = simulated typing
        public int ClipboardThreshold { get; set; } = 100; // Use clipboard for text > this length
        public bool PreferClipboard { get; set; } = false; // Always use clipboard

        /// <summary>
        /// Injects text into the currently focused window
        /// </summary>
        public async Task InjectTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Get the active window (for logging/debugging)
            IntPtr foregroundWindow = GetForegroundWindow();
            string windowTitle = GetActiveWindowTitle(foregroundWindow);
            System.Diagnostics.Debug.WriteLine($"Injecting text into: {windowTitle}");

            // Decide injection method
            if (PreferClipboard || text.Length > ClipboardThreshold || ContainsSpecialCharacters(text))
            {
                await InjectViaClipboardAsync(text, cancellationToken);
            }
            else
            {
                await InjectViaSendInputAsync(text, cancellationToken);
            }
        }

        /// <summary>
        /// Uses SendInput with Unicode characters for direct keystroke simulation
        /// </summary>
        private async Task InjectViaSendInputAsync(string text, CancellationToken cancellationToken)
        {
            foreach (char c in text)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Handle newlines as Enter key
                if (c == '\n')
                {
                    SendKeyPress(0x0D); // VK_RETURN
                }
                else if (c == '\r')
                {
                    // Skip carriage return (handle \r\n as single Enter)
                    continue;
                }
                else
                {
                    SendUnicodeChar(c);
                }

                if (TypingDelayMs > 0)
                {
                    await Task.Delay(TypingDelayMs, cancellationToken);
                }
            }
        }

        private void SendUnicodeChar(char c)
        {
            INPUT[] inputs = new INPUT[2];

            // Key down
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

            // Key up
            inputs[1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

            SendInput(2, inputs, INPUT.Size);
        }

        private void SendKeyPress(ushort vk)
        {
            INPUT[] inputs = new INPUT[2];

            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 }
                }
            };

            inputs[1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
                }
            };

            SendInput(2, inputs, INPUT.Size);
        }

        /// <summary>
        /// Uses clipboard + Ctrl+V for reliable pasting of complex/long text
        /// </summary>
        private async Task InjectViaClipboardAsync(string text, CancellationToken cancellationToken)
        {
            // Must run clipboard operations on STA thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Backup existing clipboard
                string? originalClipboard = null;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        originalClipboard = Clipboard.GetText();
                    }
                }
                catch { }

                try
                {
                    // Set our text
                    Clipboard.SetText(text);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clipboard error: {ex.Message}");
                    return;
                }

                // Small delay for clipboard to settle
                Thread.Sleep(50);

                // Send Ctrl+V
                SendCtrlV();

                // Restore original clipboard after a delay
                if (originalClipboard != null)
                {
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            try { Clipboard.SetText(originalClipboard); } catch { }
                        });
                    });
                }
            });
        }

        private void SendCtrlV()
        {
            INPUT[] inputs = new INPUT[4];

            // Ctrl down
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = 0 } }
            };

            // V down
            inputs[1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = 0 } }
            };

            // V up
            inputs[2] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } }
            };

            // Ctrl up
            inputs[3] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } }
            };

            SendInput(4, inputs, INPUT.Size);
        }

        private string GetActiveWindowTitle(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private bool ContainsSpecialCharacters(string text)
        {
            // Check for characters that might not inject well via SendInput
            foreach (char c in text)
            {
                // Emoji and high-unicode characters
                if (char.IsSurrogate(c) || c > 0xFFFF)
                    return true;
            }
            return false;
        }
    }
}
