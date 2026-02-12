using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Services;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace EliteWhisper.ViewModels
{
    public partial class ConfigurationViewModel : ObservableObject
    {
        private readonly HotkeyService _hotkeyService;
        private readonly WhisperConfigurationService _configService;
        private readonly HistoryService _historyService;
        private Action? _requestFocus;

        [ObservableProperty]
        private string _currentHotkeyDisplay = "F2";

        [ObservableProperty]
        private bool _isCapturing;

        [ObservableProperty]
        private string _historyPath = string.Empty;

        public ConfigurationViewModel(HotkeyService hotkeyService, WhisperConfigurationService configService, HistoryService historyService)
        {
            _hotkeyService = hotkeyService;
            _configService = configService;
            _historyService = historyService;
            
            UpdateHotkeyDisplay();
            
            // Load initial history path
            HistoryPath = _configService.CurrentConfiguration.HistoryStoragePath 
                           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EliteWhisper");
        }

        public void SetFocusCallback(Action requestFocus)
        {
            _requestFocus = requestFocus;
        }

        [RelayCommand]
        public void ChangeHotkey()
        {
            IsCapturing = true;
            CurrentHotkeyDisplay = "Press a key...";
            _requestFocus?.Invoke();
        }

        public void CaptureKeyPress(Key key, ModifierKeys modifiers)
        {
            if (!IsCapturing) return;

            // Ignore modifier-only keys
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            // Convert WPF Key to Virtual Key code
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            
            // Convert ModifierKeys to Win32 modifiers
            uint modifierFlags = 0;
            if ((modifiers & ModifierKeys.Control) != 0) modifierFlags |= 0x0002; // MOD_CONTROL
            if ((modifiers & ModifierKeys.Shift) != 0) modifierFlags |= 0x0004;   // MOD_SHIFT
            if ((modifiers & ModifierKeys.Alt) != 0) modifierFlags |= 0x0001;     // MOD_ALT

            // Update hotkey service
            bool success = _hotkeyService.UpdateHotkey(vk, modifierFlags);
            
            if (success)
            {
                // Save to settings (simple registry approach)
                try
                {
                    using var regKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\EliteWhisper");
                    regKey?.SetValue("HotkeyVK", vk);
                    regKey?.SetValue("HotkeyModifiers", modifierFlags);
                }
                catch { }

                UpdateHotkeyDisplay();
            }
            else
            {
                MessageBox.Show("Failed to register hotkey. It may be in use by another application.", 
                    "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateHotkeyDisplay();
            }

            IsCapturing = false;
        }

        public void LoadSavedHotkey()
        {
            try
            {
                using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\EliteWhisper");
                if (regKey != null)
                {
                    var vk = regKey.GetValue("HotkeyVK");
                    var modifiers = regKey.GetValue("HotkeyModifiers");
                    
                    if (vk != null && modifiers != null)
                    {
                        _hotkeyService.CurrentVirtualKey = Convert.ToUInt32(vk);
                        _hotkeyService.CurrentModifiers = Convert.ToUInt32(modifiers);
                        UpdateHotkeyDisplay();
                    }
                }
            }
            catch { }
        }

        private void UpdateHotkeyDisplay()
        {
            string display = "";
            
            // Add modifiers
            if ((_hotkeyService.CurrentModifiers & 0x0002) != 0) display += "Ctrl+";
            if ((_hotkeyService.CurrentModifiers & 0x0004) != 0) display += "Shift+";
            if ((_hotkeyService.CurrentModifiers & 0x0001) != 0) display += "Alt+";
            
            // Add key name
            Key key = KeyInterop.KeyFromVirtualKey((int)_hotkeyService.CurrentVirtualKey);
            display += key.ToString();
            
            CurrentHotkeyDisplay = display;
        }

        [RelayCommand]
        private void BrowseHistoryFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select a folder to store dictation history";
            dialog.UseDescriptionForTitle = true;
            dialog.SelectedPath = HistoryPath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    string path = dialog.SelectedPath;
                    _configService.SetHistoryStoragePath(path);
                    _historyService.RefreshLocation();
                    HistoryPath = path;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error setting history path: {ex.Message}", "Error");
                }
            }
        }
    }
}
