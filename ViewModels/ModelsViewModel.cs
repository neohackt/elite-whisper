using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using SharpCompress.Readers;

namespace EliteWhisper.ViewModels
{
    public partial class ModelsViewModel : ObservableObject
    {
        private readonly ModelRegistryService _registryService;
        private readonly WhisperConfigurationService _configService;
        private readonly AIEngineService _aiEngine;
        private readonly HttpClient _httpClient;

        [ObservableProperty]
        private ObservableCollection<ModelCardViewModel> _models = new();

        [ObservableProperty]
        private string _currentStoragePath = string.Empty;

        [ObservableProperty]
        private bool _isBusy;
        
        /// <summary>
        /// Single source of truth for which model is currently active.
        /// Stores the model ID (e.g., "fast", "balanced", "accurate").
        /// </summary>
        [ObservableProperty]
        private string? _activeModelId;

        public ModelsViewModel(
            ModelRegistryService registryService,
            WhisperConfigurationService configService,
            AIEngineService aiEngine)
        {
            _registryService = registryService;
            _configService = configService;
            _aiEngine = aiEngine;
            _httpClient = new HttpClient();
            
            // Listen to engine state
            _aiEngine.StateChanged += OnEngineStateChanged;
            
            // Initialize busy state
            UpdateBusyState(_aiEngine.State);
            
            // Initialize storage path
            var config = _configService.CurrentConfiguration;
            if (!string.IsNullOrEmpty(config.ModelsDirectory))
            {
                CurrentStoragePath = config.ModelsDirectory;
            }
            else
            {
                CurrentStoragePath = Path.Combine(config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "models");
            }

            LoadModels();
        }

        private void LoadModels()
        {
            Models.Clear();
            
            var config = _configService.CurrentConfiguration;
            string? modelsDir = config.ModelsDirectory;
            
            // If no models directory is configured, use default
            if (string.IsNullOrEmpty(modelsDir))
            {
                modelsDir = Path.Combine(config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "models");
            }

            // Get active model from config
            string? activeModelPath = config.DefaultModelPath;
            string? activeModelFilename = activeModelPath != null ? Path.GetFileName(activeModelPath) : null;

            foreach (var entry in _registryService.AvailableModels)
            {
                var card = new ModelCardViewModel(entry);
                
                // Check if installed by looking for the actual file or directory on disk
                if (!string.IsNullOrEmpty(modelsDir))
                {
                    string modelPath = Path.Combine(modelsDir, entry.Filename);
                    if (File.Exists(modelPath) || Directory.Exists(modelPath))
                    {
                        card.IsInstalled = true;
                    }
                }
                
                // Set ActiveModelId if this is the active model
                if (activeModelFilename != null && activeModelFilename.Equals(entry.Filename, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveModelId = entry.Id;
                }

                Models.Add(card);
            }
        }

        [RelayCommand]
        private void ChangeStorageLocation()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Model Storage Location",
                InitialDirectory = CurrentStoragePath
            };

            if (dialog.ShowDialog() == true)
            {
                string newPath = dialog.FolderName;
                try
                {
                    // Update configuration
                    var config = _configService.CurrentConfiguration;
                    config.ModelsDirectory = newPath;
                    
                    // We need a way to perform partial updates or re-apply
                    // For now, re-using validation logic but forcing the new models dir
                    var validation = _configService.DiscoverAndValidate(config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory);
                    validation.ModelsDirectory = newPath;
                    
                    // Note: This effectively just saves the new path but doesn't move files
                    // In a real app we might ask to move files. 
                    // For now, just switch the pointer.
                    _configService.ApplyConfiguration(config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, validation);
                    
                    CurrentStoragePath = newPath;
                    
                    // Refresh models (they might show as uninstalled now if files aren't there)
                    LoadModels();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to update storage location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void RevealInFolder(ModelCardViewModel card)
        {
            string? modelsDir = _configService.CurrentConfiguration.ModelsDirectory;
            if (string.IsNullOrEmpty(modelsDir)) return;

            if (card.Filename == null) return;
            string fullPath = Path.Combine(modelsDir, card.Filename);
            string argument = "/select, \"" + fullPath + "\"";

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", argument);
            }
            catch { }
        }

        [RelayCommand]
        private void DeleteModel(ModelCardViewModel card)
        {
            // Don't allow deleting the active model or non-installed models
            bool isActiveModel = card.Id.Equals(ActiveModelId, StringComparison.OrdinalIgnoreCase);
            if (!card.IsInstalled || isActiveModel) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete the '{card.Name}' model?\nThis will remove the file from your disk.",
                "Delete Model",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    string? modelsDir = _configService.CurrentConfiguration.ModelsDirectory;
                    if (string.IsNullOrEmpty(modelsDir) || string.IsNullOrEmpty(card.Filename)) return;

                    string fullPath = Path.Combine(modelsDir, card.Filename);

                    if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                    }
                    else if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        // Also delete companion .onnx_data file if exists
                        string dataFile = fullPath + "_data";
                        if (File.Exists(dataFile)) File.Delete(dataFile);
                    }

                    // Refresh
                    _configService.RevalidateCurrentConfiguration();
                    card.IsInstalled = false;
                    MessageBox.Show("Model deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task DownloadModel(ModelCardViewModel card)
        {
            if (card.IsDownloading || card.IsInstalled) return;

            try
            {
                card.IsDownloading = true;
                card.DownloadProgress = 0;

                string modelsDir = CurrentStoragePath; 
                Directory.CreateDirectory(modelsDir);

                bool isSherpaArchive = card.DownloadUrl.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(card.EngineType, "sherpa", StringComparison.OrdinalIgnoreCase);

                if (isSherpaArchive)
                {
                    await DownloadAndExtractSherpaModelAsync(card, modelsDir);
                }
                else
                {
                    await DownloadStandardModelAsync(card, modelsDir);
                }

                // Refresh config to pick up new model
                _configService.RevalidateCurrentConfiguration();
                
                card.IsInstalled = true;
                card.IsDownloading = false;
            }
            catch (Exception ex)
            {
                card.IsDownloading = false;
                card.DownloadProgress = 0;
                
                // Cleanup temp files
                try
                {
                    string modelsDir = CurrentStoragePath;
                    string tempPath = Path.Combine(modelsDir, card.Filename + ".tar.bz2.tmp");
                    if (File.Exists(tempPath)) File.Delete(tempPath);

                    string archivePath = Path.Combine(modelsDir, card.Filename + ".tar.bz2");
                    if (File.Exists(archivePath)) File.Delete(archivePath);
                }
                catch { }

                MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Downloads and extracts a Sherpa-ONNX model archive (.tar.bz2) into the models directory.
        /// </summary>
        private async Task DownloadAndExtractSherpaModelAsync(ModelCardViewModel card, string modelsDir)
        {
            long totalBytesExpected = (long)card.SizeMB * 1024 * 1024;
            if (totalBytesExpected <= 0) totalBytesExpected = 1;

            // Download archive
            string archiveTempPath = Path.Combine(modelsDir, card.Filename + ".tar.bz2.tmp");
            string archivePath = Path.Combine(modelsDir, card.Filename + ".tar.bz2");

            using (var response = await _httpClient.GetAsync(card.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long totalRead = 0;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(archiveTempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        // Download is ~80% of the work, extraction is ~20%
                        card.DownloadProgress = (double)totalRead / totalBytesExpected * 80;
                    }
                }
            }

            // Move temp to final archive name
            if (File.Exists(archivePath)) File.Delete(archivePath);
            File.Move(archiveTempPath, archivePath);

            // Extract archive
            card.DownloadProgress = 85;
            string targetDir = Path.Combine(modelsDir, card.Filename);
            
            await Task.Run(() =>
            {
                using (var stream = File.OpenRead(archivePath))
                using (var reader = SharpCompress.Readers.ReaderFactory.Open(stream))
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory)
                        {
                            reader.WriteEntryToDirectory(modelsDir, new SharpCompress.Common.ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }
            });

            card.DownloadProgress = 95;

            // Validate extracted files
            if (!Directory.Exists(targetDir))
            {
                // Cleanup archive
                if (File.Exists(archivePath)) File.Delete(archivePath);
                throw new Exception($"Extraction failed: directory '{card.Filename}' not found after extracting archive.");
            }

            // Verify required model files exist
            bool hasEncoder = Directory.GetFiles(targetDir, "encoder*.onnx").Length > 0;
            bool hasDecoder = Directory.GetFiles(targetDir, "decoder*.onnx").Length > 0;
            bool hasJoiner = Directory.GetFiles(targetDir, "joiner*.onnx").Length > 0;
            bool hasTokens = File.Exists(Path.Combine(targetDir, "tokens.txt"));

            if (!hasEncoder || !hasDecoder || !hasJoiner || !hasTokens)
            {
                // Cleanup
                try { Directory.Delete(targetDir, true); } catch { }
                if (File.Exists(archivePath)) File.Delete(archivePath);
                throw new Exception("Incomplete model: extracted files are missing encoder, decoder, joiner, or tokens.txt.");
            }

            // Cleanup archive to save disk space
            try { File.Delete(archivePath); } catch { }

            card.DownloadProgress = 100;
        }

        /// <summary>
        /// Downloads standard ONNX model files (Whisper .bin or Parakeet .onnx + .onnx_data).
        /// </summary>
        private async Task DownloadStandardModelAsync(ModelCardViewModel card, string modelsDir)
        {
            var filesToDownload = new System.Collections.Generic.List<(string Url, string Filename)>
            {
                (card.DownloadUrl, card.Filename)
            };

            if (!string.IsNullOrEmpty(card.DataDownloadUrl) && !string.IsNullOrEmpty(card.DataFilename))
            {
                filesToDownload.Add((card.DataDownloadUrl, card.DataFilename));
            }

            long totalBytesAllFiles = (long)card.SizeMB * 1024 * 1024;
            if (totalBytesAllFiles <= 0) totalBytesAllFiles = 1;

            long totalReadSoFar = 0;

            foreach (var file in filesToDownload)
            {
                string targetPath = Path.Combine(modelsDir, file.Filename);
                string tempPath = targetPath + ".tmp";

                using (var response = await _httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        int read;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalReadSoFar += read;
                            card.DownloadProgress = (double)totalReadSoFar / totalBytesAllFiles * 100;
                        }
                    }
                }

                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }

            // Validation
            try 
            {
                string mainTargetPath = Path.Combine(modelsDir, card.Filename);

                if (!string.IsNullOrEmpty(card.Sha256))
                {
                    string calculatedHash = await ComputeSha256HashAsync(mainTargetPath);
                    if (!string.Equals(calculatedHash, card.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"Checksum mismatch.\nExpected: {card.Sha256}\nActual: {calculatedHash}");
                    }
                }
                else
                {
                    long actualSizeMB = 0;
                    foreach (var f in filesToDownload)
                    {
                        actualSizeMB += new FileInfo(Path.Combine(modelsDir, f.Filename)).Length;
                    }
                    actualSizeMB /= (1024 * 1024);
                    
                    if (card.SizeMB > 0)
                    {
                         if (Math.Abs(actualSizeMB - card.SizeMB) > 50 && actualSizeMB < 10)
                         {
                             throw new Exception($"File size validation failed. Expected ~{card.SizeMB} MB, Got {actualSizeMB} MB.");
                         }
                    }
                }
            }
            catch (Exception validationEx)
            {
                foreach (var file in filesToDownload)
                {
                    string p = Path.Combine(modelsDir, file.Filename);
                    if (File.Exists(p)) File.Delete(p);
                }
                throw new Exception($"Validation failed: {validationEx.Message}");
            }
        }

        private async Task<string> ComputeSha256HashAsync(string filePath)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hashBytes = await sha256.ComputeHashAsync(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }



        private void OnEngineStateChanged(object? sender, EngineState state)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateBusyState(state);
                
                if (state == EngineState.Ready)
                {
                    // Refresh ActiveModelId from config
                    string? activePath = _configService.CurrentConfiguration.DefaultModelPath;
                    string? activeFilename = activePath != null ? Path.GetFileName(activePath) : null;
                    
                    // Find the model card with matching filename and set ActiveModelId
                    if (activeFilename != null)
                    {
                        var activeCard = Models.FirstOrDefault(m => 
                            m.Filename.Equals(activeFilename, StringComparison.OrdinalIgnoreCase));
                        
                        if (activeCard != null)
                        {
                            ActiveModelId = activeCard.Id;
                        }
                    }
                }
            });
        }

        private void UpdateBusyState(EngineState state)
        {
            IsBusy = state == EngineState.Loading || state == EngineState.Recording || state == EngineState.Processing;
        }

        [RelayCommand]
        private async Task ActivateModel(ModelCardViewModel card)
        {
            if (!card.IsInstalled || IsBusy) return;

            string? modelsDir = _configService.CurrentConfiguration.ModelsDirectory;
            if (string.IsNullOrEmpty(modelsDir) || string.IsNullOrEmpty(card.Filename)) return;

            string fullPath = Path.Combine(modelsDir, card.Filename);
            
            try 
            {
                // Sherpa models are activated differently — they don't go through the Whisper engine.
                // Instead, we set the STT engine preference and the SpeechEngineSelector picks them up.
                if (string.Equals(card.EngineType, "sherpa", StringComparison.OrdinalIgnoreCase))
                {
                    var config = _configService.CurrentConfiguration;
                    config.PreferredSTTEngine = "Sherpa";
                    config.AutoSelectSTT = false;
                    config.DefaultModelPath = fullPath; // Crucial for UI resolving ActiveModelId
                    _configService.SaveConfiguration(config);

                    ActiveModelId = card.Id;
                    MessageBox.Show("Parakeet (Sherpa) is now the active speech engine.\nIt will be used for your next transcription.", 
                        "Engine Activated", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Legacy Whisper / Parakeet ONNX models — activate via engine service
                    bool success = await _aiEngine.ActivateModelAsync(fullPath);
                    
                    if (success)
                    {
                        ActiveModelId = card.Id;
                    }
                    else
                    {
                        MessageBox.Show("Unable to activate model. Please try again.", "Activation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Unable to activate model. Please try again.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
