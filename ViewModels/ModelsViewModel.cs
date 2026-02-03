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
                
                // Check if installed by looking for the actual file on disk
                if (!string.IsNullOrEmpty(modelsDir))
                {
                    string modelPath = Path.Combine(modelsDir, entry.Filename);
                    if (File.Exists(modelPath))
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

                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
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

                // Use current configured path
                string modelsDir = CurrentStoragePath; 
                Directory.CreateDirectory(modelsDir);

                string targetPath = Path.Combine(modelsDir, card.Filename);
                string tempPath = targetPath + ".tmp";

                // Download logic with progress
                using (var response = await _httpClient.GetAsync(card.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? 1L;
                    
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var isMoreToRead = true;
                        var totalRead = 0L;

                        do
                        {
                            var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0)
                            {
                                isMoreToRead = false;
                            }
                            else
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                card.DownloadProgress = (double)totalRead / totalBytes * 100;
                            }
                        }
                        while (isMoreToRead);
                    }
                }

                // Move temp file to final destination
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tempPath, targetPath);

                // Validation Phase
                try 
                {
                    if (!string.IsNullOrEmpty(card.Sha256))
                    {
                        // Checksum validation
                        string calculatedHash = await ComputeSha256HashAsync(targetPath);
                        if (!string.Equals(calculatedHash, card.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new Exception($"Checksum mismatch.\nExpected: {card.Sha256}\nActual: {calculatedHash}");
                        }
                    }
                    else
                    {
                        // Fallback to size validation 
                        var fileInfo = new FileInfo(targetPath);
                        long sizeMB = fileInfo.Length / (1024 * 1024);
                        
                        // Stricter check: if defined size is > 0
                        if (card.SizeMB > 0)
                        {
                             // Logic: Fails if difference > 50MB AND file is < 10MB (too small)
                             // Or simplified: it must be reasonably close.
                             // Let's stick to the established generous check but throw if failed
                             if (Math.Abs(sizeMB - card.SizeMB) > 50 && sizeMB < 10)
                             {
                                 throw new Exception($"File size validation failed. Expected ~{card.SizeMB} MB, Got {sizeMB} MB.");
                             }
                        }
                    }
                }
                catch (Exception validationEx)
                {
                    // Cleanup invalid file
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    throw new Exception($"Validation failed: {validationEx.Message}");
                }

                // Refresh config to pick up new model
                _configService.RevalidateCurrentConfiguration();
                
                card.IsInstalled = true;
                card.IsDownloading = false;
                
                // Auto-activate if it's the only one or user requested? 
                // For now just mark installed.
            }
            catch (Exception ex)
            {
                card.IsDownloading = false;
                card.DownloadProgress = 0;
                
                // Ensure temp file is cleaned up on error
                string modelsDir = CurrentStoragePath; 
                string targetPath = Path.Combine(modelsDir, card.Filename);
                string tempPath = targetPath + ".tmp";
                if (File.Exists(tempPath)) File.Delete(tempPath);

                MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // Activate the model via engine service
                bool success = await _aiEngine.ActivateModelAsync(fullPath);
                
                if (success)
                {
                    // Update single source of truth
                    ActiveModelId = card.Id;
                }
                else
                {
                    MessageBox.Show("Unable to activate model. Please try again.", "Activation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Unable to activate model. Please try again.\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
