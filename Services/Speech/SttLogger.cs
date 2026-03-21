using System;
using System.IO;

namespace EliteWhisper.Services.Speech
{
    public static class SttLogger
    {
        private static readonly string LogFile = @"d:\Personal\voiceapp\stt_debug.log";
        private static readonly object _lock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
