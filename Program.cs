using System;
using System.Windows;

namespace EliteWhisper
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                Log("Program.Main Started");
                var app = new App();
                Log("App Instance Created");
                app.InitializeComponent();
                Log("App Initialized Component");
                app.Run();
            }
            catch (Exception ex)
            {
                Log($"CRITICAL FAILURE IN MAIN: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"CRITICAL FAILURE: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void Log(string message)
        {
            try
            {
                string logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.txt");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                System.IO.File.AppendAllText(logFile, $"[{timestamp}] {message}{Environment.NewLine}");
            }
            catch { /* Best effort logging */ }
        }
    }
}
