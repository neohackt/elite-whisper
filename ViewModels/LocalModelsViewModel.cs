using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using EliteWhisper.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;

namespace EliteWhisper.ViewModels
{
    public partial class LocalModelsViewModel : ObservableObject
    {
        private readonly LocalModelService _modelService;
        private readonly ModelDownloadService _downloadService;
        private readonly LlamaCppService _llamaService;
        private readonly WhisperConfigurationService _configService;

        [ObservableProperty]
        private ObservableCollection<LocalModelInfo> _installedModels = new();

        [ObservableProperty]
        private ObservableCollection<DownloadableModelInfo> _availableModels = new();

        [ObservableProperty]
        private LlamaRuntimeSettings _settings;

        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _benchmarkResult = string.Empty;

        [ObservableProperty]
        private bool _isBenchmarking;

        // Settings Properties for UI Binding
        [ObservableProperty]
        private int _threadCount;
        
        [ObservableProperty]
        private int _contextSize;

        [ObservableProperty]
        private double _temperature;
        
        [ObservableProperty]
        private string _localModelsPath;

        private CancellationTokenSource? _downloadCts;

        public IAsyncRelayCommand<DownloadableModelInfo> DownloadCommand { get; }

        public LocalModelsViewModel(
            LocalModelService modelService,
            ModelDownloadService downloadService,
            LlamaCppService llamaService,
            WhisperConfigurationService configService)
        {
            _modelService = modelService;
            _downloadService = downloadService;
            _llamaService = llamaService;
            _configService = configService;

            Settings = _configService.CurrentConfiguration.LlamaSettings;
            LocalModelsPath = _modelService.ModelsPath;
            
            // Init bound properties
            ThreadCount = Settings.Threads;
            ContextSize = Settings.ContextSize;
            Temperature = Settings.Temperature;

            Temperature = Settings.Temperature;

            DownloadCommand = new AsyncRelayCommand<DownloadableModelInfo>(DownloadModel);
            RefreshModels();
        }

        public void RefreshModels()
        {
             InstalledModels = new ObservableCollection<LocalModelInfo>(_modelService.GetInstalledModels());
             AvailableModels = new ObservableCollection<DownloadableModelInfo>(_modelService.GetAvailableModels());
        }

        private async Task DownloadModel(DownloadableModelInfo? model)
        {
            if (model == null) return;
            Debug.WriteLine($"[DownloadModel] Command Fired for {model.DisplayName}");

            if (IsDownloading || model.IsDownloading) return;

            // Check if already installed
            if (_modelService.IsModelInstalled(model.FileName))
            {
                MessageBox.Show($"{model.FileName} is already installed.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsDownloading = true; // Global lock
            model.IsDownloading = true; // Local UI state
            model.Status = "Downloading...";
            StatusMessage = $"Downloading {model.DisplayName}...";
            DownloadProgress = 0;
            model.DownloadProgress = 0;
            _downloadCts = new CancellationTokenSource();

            string destPath = Path.Combine(_modelService.ModelsPath, model.FileName);

            try
            {
                var progress = new Progress<double>(p => 
                {
                    DownloadProgress = p;
                    model.DownloadProgress = p;
                });
                await _downloadService.DownloadModelAsync(model.DownloadUrl, destPath, progress, _downloadCts.Token);
                
                StatusMessage = "Download Complete";
                model.Status = "Installed";
                RefreshModels();
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download Canceled";
                model.Status = "Canceled";
                // Clean up partial file
                if (File.Exists(destPath)) File.Delete(destPath);
            }
            catch (Exception ex)
            {
                StatusMessage = "Download Error";
                model.Status = "Error";
                MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                if (File.Exists(destPath)) File.Delete(destPath);
            }
            finally
            {
                IsDownloading = false;
                model.IsDownloading = false;
                _downloadCts = null;
            }
        }

        [RelayCommand]
        private void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        [RelayCommand]
        private void DeleteModel(LocalModelInfo model)
        {
            if (MessageBox.Show($"Are you sure you want to delete {model.Name}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _modelService.DeleteModel(model);
                RefreshModels();
            }
        }

        [RelayCommand]
        private async Task BenchmarkModel(LocalModelInfo model)
        {
            if (IsBenchmarking) return;
            IsBenchmarking = true;
            BenchmarkResult = "Running...";
            
            try
            {
                var sw = Stopwatch.StartNew();
                // Simple prompt to test throughput
                string prompt = "Count from 1 to 20."; 
                var options = new LlmOptions { MaxTokens = 100, Temperature = 0.1f };
                
                var result = await _llamaService.GenerateAsync(model.FilePath, prompt, options, CancellationToken.None);
                
                sw.Stop();
                BenchmarkResult = $"{sw.ElapsedMilliseconds}ms";
                
                MessageBox.Show($"Benchmark Complete:\nTime: {sw.ElapsedMilliseconds}ms\nOutput: {result}", "Benchmark", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                BenchmarkResult = "Error";
                MessageBox.Show($"Benchmark failed: {ex.Message}", "Error");
            }
            finally
            {
                IsBenchmarking = false;
            }
        }
        
        [RelayCommand]
        private void SaveSettings()
        {
            Settings.Threads = ThreadCount;
            Settings.ContextSize = ContextSize;
            Settings.Temperature = Temperature;
            
            _configService.SaveConfiguration(_configService.CurrentConfiguration);
            _configService.SaveConfiguration(_configService.CurrentConfiguration);
            StatusMessage = "Settings Saved";
        }

        [RelayCommand]
        private void BrowseModelsPath()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a folder to store AI models";
                dialog.UseDescriptionForTitle = true;
                dialog.ShowNewFolderButton = true;
                
                if (Directory.Exists(LocalModelsPath))
                {
                    dialog.SelectedPath = LocalModelsPath;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string newPath = dialog.SelectedPath;
                    _modelService.UpdateModelsPath(newPath);
                    LocalModelsPath = newPath;
                    StatusMessage = "Model Path Updated";
                    RefreshModels();
                    
                    MessageBox.Show("Model storage location updated.\n\nNote: Existing models were NOT moved. You may need to move them manually or download them again.", "Storage Changed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}
