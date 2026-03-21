using System;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using NetSparkleUpdater;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WPF;
using NetSparkleUpdater.Enums;

namespace EliteWhisper.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly SparkleUpdater _sparkle;
        private const string APPCAST_URL = "https://raw.githubusercontent.com/neohackt/elite-whisper/main/appcast.xml";

        public UpdateService()
        {
            // Set download path to LocalAppData/EliteWhisper/Updates
            string downloadPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EliteWhisper",
                "Updates");

            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

            // Initialize SparkleUpdater with Ed25519Checker
            // Note: SecurityMode.Unsafe allows unsigned updates (for testing). Use Strict for production with keys.
            var signatureVerifier = new Ed25519Checker(SecurityMode.Unsafe); 
            
            _sparkle = new SparkleUpdater(APPCAST_URL, signatureVerifier)
            {
                UIFactory = new NetSparkleUpdater.UI.WPF.UIFactory(),
                //RelaunchAfterUpdate = true,
                //CustomInstallerArguments = "/SILENT", // For Inno Setup if needed
            };
            

            
            // Configure logging
            _sparkle.LogWriter = new NetSparkleUpdater.LogWriter(LogWriterOutputMode.Debug); 
        }

        public void Start()
        {
            try
            {
                // Start the background loop to check for updates every 24 hours
                _sparkle.StartLoop(true, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateService Start Error: {ex.Message}");
            }
        }

        public async Task CheckForUpdatesAsync()
        {
            try 
            {
                await _sparkle.CheckForUpdatesAtUserRequest();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateService Check Error: {ex.Message}");
                MessageBox.Show($"Could not check for updates: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
