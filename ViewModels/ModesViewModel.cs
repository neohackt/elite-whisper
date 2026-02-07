using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using EliteWhisper.Services;

namespace EliteWhisper.ViewModels
{
    /// <summary>
    /// ViewModel for Modes page - manages dictation mode selection.
    /// Uses single source of truth pattern: ActiveModeId.
    /// </summary>
    public partial class ModesViewModel : ObservableObject
    {
        private readonly ModeService _modeService;
        private readonly Services.LLM.OllamaProvider _ollamaProvider;
        private readonly Services.LLM.OpenRouterProvider _openRouterProvider;
        
        /// <summary>
        /// Single source of truth for active mode.
        /// UI elements derive their state by comparing with this ID.
        /// </summary>
        [ObservableProperty]
        private string? _activeModeId;

        /// <summary>
        /// Available local models for dropdown.
        /// </summary>
        public ObservableCollection<string> AvailableLocalModels { get; } = new();

        /// <summary>
        /// Available Gemini models for dropdown.
        /// </summary>
        public ObservableCollection<GeminiModelInfo> AvailableGeminiModels { get; } = new();

        /// <summary>
        /// Available OpenRouter models for dropdown.
        /// </summary>
        public ObservableCollection<Services.LLM.OpenRouterModelInfo> AvailableOpenRouterModels { get; } = new();

        private readonly WhisperConfigurationService _configService;

        /// <summary>
        /// Available dictation modes.
        /// </summary>
        public ObservableCollection<DictationMode> Modes { get; } = new();
        
        public ModesViewModel(
            ModeService modeService, 
            Services.LLM.OllamaProvider ollamaProvider,
            Services.LLM.OpenRouterProvider openRouterProvider,
            WhisperConfigurationService configService)
        {
            _modeService = modeService;
            _ollamaProvider = ollamaProvider;
            _openRouterProvider = openRouterProvider;
            _configService = configService;
            
            LoadModes();
            LoadLocalModelsAsync();
            LoadGeminiModels();
        }

        [RelayCommand]
        private void SaveModeSettings()
        {
            _modeService.SaveModes();
            
            // Requirement: Refresh models when user interacts (e.g. selects Local/Gemini)
            // This covers "Ollama becomes available" scenario
            LoadLocalModelsAsync();
            // Refresh Gemini too in case they just added key
            LoadGeminiModels();
            LoadOpenRouterModelsAsync();
        }
        
        /// <summary>
        /// Load modes from service and set active mode.
        /// </summary>
        private void LoadModes()
        {
            Modes.Clear();
            
            foreach (var mode in _modeService.Modes)
            {
                Modes.Add(mode);
            }
            
            // Set active mode ID (single source of truth)
            ActiveModeId = _modeService.ActiveMode.Id;
        }

        public async void LoadLocalModelsAsync()
        {
            try
            {
                // Requirement: Query Ollama
                var models = await _ollamaProvider.GetInstalledModelsAsync();
                
                // Requirement: Sync ObservableCollection (Avoid Clear which breaks selection)
                var currentNames = AvailableLocalModels.ToList();
                var newNames = models.Select(m => m.Name).ToList();

                // Remove deleted
                foreach (var name in currentNames)
                {
                    if (!newNames.Contains(name))
                        AvailableLocalModels.Remove(name);
                }

                // Add new
                foreach (var name in newNames)
                {
                    if (!AvailableLocalModels.Contains(name))
                        AvailableLocalModels.Add(name);
                }
                
                // Debug logging
                System.Diagnostics.Debug.WriteLine($"[ModesViewModel] Loaded {models.Count} local models.");
            }
            catch (System.Exception ex)
            { 
                 System.Diagnostics.Debug.WriteLine($"[ModesViewModel] Failed to load local models: {ex.Message}");
            }
        }
        
        // Removed typo


        public async void LoadOpenRouterModelsAsync()
        {
            try
            {
                var models = await _openRouterProvider.GetAvailableModelsAsync();
                
                // Sync OpenRouter Models
                var newModels = models.OrderBy(x => x.Name).ToList();
                var currentIds = AvailableOpenRouterModels.Select(x => x.Id).ToList();
                
                // Remove deleted
                for (int i = AvailableOpenRouterModels.Count - 1; i >= 0; i--)
                {
                    if (!newModels.Any(m => m.Id == AvailableOpenRouterModels[i].Id))
                        AvailableOpenRouterModels.RemoveAt(i);
                }

                // Add new (simple check by ID)
                foreach (var m in newModels)
                {
                    if (!AvailableOpenRouterModels.Any(x => x.Id == m.Id))
                        AvailableOpenRouterModels.Add(m);
                }
                
                System.Diagnostics.Debug.WriteLine($"[ModesViewModel] Loaded {models.Count} OpenRouter models.");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModesViewModel] Failed to load OpenRouter models: {ex.Message}");
            }
        }

        private void LoadGeminiModels()
        {
            try
            {
                var cached = _configService.CurrentConfiguration.GeminiModels;
                
                if (cached != null)
                {
                    var newModels = cached.ToList();
                    
                    // Sync Gemini Models
                    for (int i = AvailableGeminiModels.Count - 1; i >= 0; i--)
                    {
                        if (!newModels.Any(m => m.Id == AvailableGeminiModels[i].Id))
                            AvailableGeminiModels.RemoveAt(i);
                    }

                    foreach (var m in newModels)
                    {
                        if (!AvailableGeminiModels.Any(x => x.Id == m.Id))
                            AvailableGeminiModels.Add(m);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModesViewModel] Failed to load Gemini models: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Activate a dictation mode.
        /// </summary>
        [RelayCommand]
        private void ActivateMode(DictationMode mode)
        {
            if (mode == null) return;
            
            // Update single source of truth
            ActiveModeId = mode.Id;
            
            // Persist to service
            _modeService.SetActiveMode(mode);
        }
        
        /// <summary>
        /// Toggle post-processing for a mode.
        /// </summary>
        [RelayCommand]
        private void TogglePostProcessing(DictationMode mode)
        {
            if (mode == null) return;
            
            mode.EnablePostProcessing = !mode.EnablePostProcessing;
            
            // Save changes
            _modeService.SaveModes();
        }
    }
}
