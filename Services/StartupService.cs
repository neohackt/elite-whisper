using Microsoft.Win32;
using System;
using System.Reflection;

namespace EliteWhisper.Services
{
    public static class StartupService
    {
        private const string AppName = "EliteWhisper";
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// Check if app is set to run at Windows startup
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Enable or disable Windows startup
        /// </summary>
        public static bool SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                if (key == null) return false;

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup registry error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the current executable path
        /// </summary>
        public static string GetExecutablePath()
        {
            return Environment.ProcessPath ?? string.Empty;
        }
    }
}
