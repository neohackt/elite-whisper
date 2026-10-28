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
        /// <summary>
        /// Base64-encoded Ed25519 public key that matches the private key used to sign the
        /// appcast and every published release artifact. An empty value means this build has
        /// no trusted signing key configured, and remote updates are disabled entirely.
        ///
        /// To enable updates:
        ///   1. Generate an Ed25519 key pair for release signing and keep the private key secret.
        ///   2. Paste the base64 public key below.
        ///   3. Sign the appcast and each release artifact with the matching private key, and
        ///      publish the appcast's detached signature next to appcast.xml.
        ///
        /// The verifier is always SecurityMode.Strict, so an update whose signature cannot be
        /// verified against this key is never installed. There is no unsigned fallback.
        /// </summary>
        private const string UpdateSigningPublicKey = "";

        private const string APPCAST_URL = "https://raw.githubusercontent.com/neohackt/elite-whisper/main/appcast.xml";
        private const string RELEASES_PAGE_URL = "https://github.com/neohackt/elite-whisper/releases";

        private const string DISABLED_EXPLANATION =
            "Automatic updates are disabled because this build of Elite Whisper has no release-signing " +
            "key configured. Elite Whisper never installs an update that it cannot verify, so no update " +
            "can be offered until a trusted signing key is available.\n\n" +
            "Please download the latest version manually from the official releases page.";

        private readonly SparkleUpdater _sparkle;
        private readonly bool _remoteUpdatesEnabled;

        /// <summary>
        /// True only when a trusted release-signing public key is configured, i.e. when a
        /// downloaded update can be signature-verified before it is installed.
        /// </summary>
        public bool RemoteUpdatesEnabled => _remoteUpdatesEnabled;

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

            // Initialize SparkleUpdater with Ed25519Checker in SecurityMode.Strict.
            // Strict fails closed: the appcast and the downloaded installer must both carry a
            // signature that verifies against the configured public key. It never degrades to
            // accepting unsigned files the way SecurityMode.Unsafe and SecurityMode.UseIfPossible
            // (without a key) do.
            //
            // publicKeyFile is passed as null on purpose. The default ("NetSparkle_Ed25519.pub")
            // makes NetSparkle look for that filename in the working directory, which would let a
            // stray file on disk silently become the trusted key. Only the key compiled in above
            // is ever used.
            var signatureVerifier = new Ed25519Checker(
                SecurityMode.Strict,
                UpdateSigningPublicKey,
                publicKeyFile: null);

            _sparkle = new SparkleUpdater(APPCAST_URL, signatureVerifier)
            {
                UIFactory = new NetSparkleUpdater.UI.WPF.UIFactory(),
                //RelaunchAfterUpdate = true,
                //CustomInstallerArguments = "/SILENT", // For Inno Setup if needed
            };

            // Configure logging
            _sparkle.LogWriter = new NetSparkleUpdater.LogWriter(LogWriterOutputMode.Debug);

            _remoteUpdatesEnabled = !string.IsNullOrWhiteSpace(UpdateSigningPublicKey);

            if (!_remoteUpdatesEnabled)
            {
                DebugHelper.Log("[UpdateService] Remote updates DISABLED: no release-signing public key is " +
                                "configured for this build. The signature verifier is in SecurityMode.Strict, " +
                                "so an unsigned update can never be installed. Users must download new " +
                                "versions manually.");
            }
        }

        public void Start()
        {
            if (!_remoteUpdatesEnabled)
            {
                DebugHelper.Log("[UpdateService] Start(): skipped - remote updates are disabled (no trusted signing key).");
                return;
            }

            try
            {
                // Start the background loop to check for updates every 24 hours
                _sparkle.StartLoop(true, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateService Start Error: {ex.Message}");
                DebugHelper.Log($"[UpdateService] Start Error: {ex.Message}");
            }
        }

        public async Task CheckForUpdatesAsync()
        {
            if (!_remoteUpdatesEnabled)
            {
                DebugHelper.Log("[UpdateService] CheckForUpdatesAsync(): blocked - remote updates are " +
                                "disabled (no trusted signing key). No appcast was fetched and no update " +
                                "can be installed.");
                MessageBox.Show(
                    $"{DISABLED_EXPLANATION}\n\n{RELEASES_PAGE_URL}",
                    "Updates Disabled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try 
            {
                await _sparkle.CheckForUpdatesAtUserRequest();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateService Check Error: {ex.Message}");
                DebugHelper.Log($"[UpdateService] Check Error: {ex.Message}");
                MessageBox.Show($"Could not check for updates: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
