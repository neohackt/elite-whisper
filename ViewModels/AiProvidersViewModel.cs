using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using EliteWhisper.Services;
using System.Net.Http;
using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using EliteWhisper.Services.LLM;

namespace EliteWhisper.ViewModels
{
    public partial class AiProvidersViewModel : ObservableObject
    {
        private readonly WhisperConfigurationService _configService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly OllamaProvider _ollamaProvider;
        private readonly GeminiProvider _geminiProvider;

        // Status Status
        [ObservableProperty]
        private bool _isOllamaRunning;
        
        [ObservableProperty]
        private bool _isGeminiConfigured;
        
        [ObservableProperty]
        private bool _isOpenRouterConfigured;

        [ObservableProperty]
        private string _geminiStatusText = "";

        // Local Models
        public ObservableCollection<LocalModelInfo> LocalModels { get; } = new();

        [ObservableProperty]
        private string _newModelName = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
        private bool _isDownloading;

        public bool IsNotDownloading => !IsDownloading;

        [ObservableProperty]
        private int _downloadProgress;

        [ObservableProperty]
        private string _downloadStatusText = "";

        // Write-Only Inputs
        [ObservableProperty]
        private string _geminiApiKeyInput = "";
        
        [ObservableProperty]
        private string _openRouterApiKeyInput = "";

        // Default Preference
        [ObservableProperty]
        private PreferredProvider _defaultProviderPreference;

        public AiProvidersViewModel(
            WhisperConfigurationService configService, 
            IHttpClientFactory httpClientFactory,
            OllamaProvider ollamaProvider,
            GeminiProvider geminiProvider)
        {
            _configService = configService;
            _httpClientFactory = httpClientFactory;
            _ollamaProvider = ollamaProvider;
            _geminiProvider = geminiProvider;
            
            LoadConfiguration();
            CheckOllamaStatusAsync().ConfigureAwait(false);
            RefreshModelsCommand.ExecuteAsync(null);

            // Auto-load Gemini if key exists
            if (IsGeminiConfigured)
            {
                 RefreshGeminiModelsCommand.ExecuteAsync(null);
            }
        }

        private void LoadConfiguration()
        {
            var config = _configService.CurrentConfiguration;
            
            // Check if keys are present (don't show them!)
            IsGeminiConfigured = !string.IsNullOrEmpty(config.GeminiApiKey);
            IsOpenRouterConfigured = !string.IsNullOrEmpty(config.OpenRouterApiKey);
            
            DefaultProviderPreference = config.DefaultProviderPreference;
        }

        partial void OnDefaultProviderPreferenceChanged(PreferredProvider value)
        {
            var config = _configService.CurrentConfiguration;
            if (config.DefaultProviderPreference != value)
            {
                config.DefaultProviderPreference = value;
                _configService.SaveConfiguration(config);
            }
        }

        [RelayCommand]
        private async Task CheckOllamaStatusAsync()
        {
            try
            {
                // Simple ping to Ollama default port
                using var client = _httpClientFactory.CreateClient("Ollama");
                using var response = await client.GetAsync("http://localhost:11434");
                IsOllamaRunning = response.IsSuccessStatusCode;
                if (IsOllamaRunning)
                {
                    await RefreshModels();
                }
            }
            catch
            {
                IsOllamaRunning = false;
            }
        }

        [RelayCommand]
        private async Task RefreshModels()
        {
            if (!IsOllamaRunning) return;
            
            try
            {
                var models = await _ollamaProvider.GetInstalledModelsAsync();
                LocalModels.Clear();
                foreach (var model in models)
                {
                    LocalModels.Add(model);
                }
            }
            catch
            {
                // Ignore errors during refresh
            }
        }

        [RelayCommand]
        private async Task DownloadModel()
        {
            if (string.IsNullOrWhiteSpace(NewModelName)) return;
            if (IsDownloading) return;

            try
            {
                IsDownloading = true;
                DownloadProgress = 0;
                // Sanitize input: remove "ollama pull" or "ollama run" if user pasted the whole command
                var modelToPull = NewModelName.Trim();
                if (modelToPull.StartsWith("ollama pull ", System.StringComparison.OrdinalIgnoreCase))
                    modelToPull = modelToPull.Substring(12).Trim();
                if (modelToPull.StartsWith("ollama run ", System.StringComparison.OrdinalIgnoreCase))
                    modelToPull = modelToPull.Substring(11).Trim();

                if (string.IsNullOrWhiteSpace(modelToPull))
                {
                     DownloadStatusText = "Invalid model name";
                     IsDownloading = false;
                     return;
                }

                DownloadStatusText = $"Pulling {modelToPull}...";

                var progress = new Progress<int>(p => DownloadProgress = p);
                await _ollamaProvider.PullModelAsync(modelToPull, progress);
                
                DownloadStatusText = "Done!";
                NewModelName = ""; // Clear input
                await RefreshModels();
            }
            catch (System.Exception ex)
            {
                 DownloadStatusText = $"Error: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
                // Keep status text visible for a moment? Or just leave it.
            }
        }

        [RelayCommand]
        private async Task DeleteModel(LocalModelInfo model)
        {
            if (model == null) return;
            
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete '{model.Name}'?", 
                "Delete Model", 
                System.Windows.MessageBoxButton.YesNo, 
                System.Windows.MessageBoxImage.Warning);
                
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    await _ollamaProvider.DeleteModelAsync(model.Name);
                    LocalModels.Remove(model);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to delete model: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task RefreshGeminiModels()
        {
            if (!IsGeminiConfigured) 
            {
                GeminiStatusText = "API Key needed";
                return;
            }

            try
            {
                GeminiStatusText = "Loading models...";
                var result = await _geminiProvider.GetAvailableModelsAsync();
                var models = result.Models;
                var debugLog = result.DebugLog;
                
                if (models.Count > 0)
                {
                    // Cache to config
                    var config = _configService.CurrentConfiguration;
                    config.GeminiModels = models;
                    _configService.SaveConfiguration(config);
                    
                    GeminiStatusText = $"{models.Count} models loaded";
                }
                else
                {
                    // Show full debug info to user
                    var lastLine = debugLog.Trim().Split('\n').LastOrDefault() ?? "Unknown error";
                    GeminiStatusText = $"Failed: {lastLine}";
                    
                    System.Windows.MessageBox.Show(
                        $"Gemini Model Discovery Failed.\n\nDebug Log:\n{debugLog}", 
                        "Gemini Debug Info", 
                        System.Windows.MessageBoxButton.OK, 
                        System.Windows.MessageBoxImage.Warning);
                        
                    System.Diagnostics.Debug.WriteLine($"Gemini Refresh Debug: {debugLog}");
                }
            }
            catch (System.Exception ex)
            {
                GeminiStatusText = $"Error: {ex.Message}";
                System.Windows.MessageBox.Show($"Unexpected Error:\n{ex}", "Gemini Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task SaveKeys()
        {
            bool changed = false;
            var config = _configService.CurrentConfiguration;

            // Only update if input is provided
            if (!string.IsNullOrWhiteSpace(GeminiApiKeyInput))
            {
                config.GeminiApiKey = GeminiApiKeyInput; // Setter will encrypt on Save
                IsGeminiConfigured = true;
                GeminiApiKeyInput = ""; // Clear input immediately
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(OpenRouterApiKeyInput))
            {
                config.OpenRouterApiKey = OpenRouterApiKeyInput; // Setter will encrypt on Save
                IsOpenRouterConfigured = true;
                OpenRouterApiKeyInput = ""; // Clear input immediately
                changed = true;
            }

            if (changed)
            {
                _configService.SaveConfiguration(config);
                System.Windows.MessageBox.Show("API keys saved securely.", "Configuration Saved", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                
                if (IsGeminiConfigured)
                {
                    await RefreshGeminiModels();
                }
            }
        }
    }
}
