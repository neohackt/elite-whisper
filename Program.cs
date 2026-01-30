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
                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"FATAL ERROR: {ex.Message}\n\n{ex.StackTrace}", "Elite Whisper Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
