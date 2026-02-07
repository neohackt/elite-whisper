using System;
using System.IO;

namespace EliteWhisper.Services
{
    public static class DebugHelper
    {
        private static string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "elitewhisper_debug.txt");

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
