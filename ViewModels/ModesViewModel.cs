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
        /// Available dictation modes.
        /// </summary>
        public ObservableCollection<DictationMode> Modes { get; } = new();
        
        public ModesViewModel(ModeService modeService, Services.LLM.OllamaProvider ollamaProvider)
        {
            _modeService = modeService;
            _ollamaProvider = ollamaProvider;
            LoadModes();
            LoadLocalModelsAsync();
        }

        private async void LoadLocalModelsAsync()
        {
            try
            {
                var models = await _ollamaProvider.GetInstalledModelsAsync();
                AvailableLocalModels.Clear();
                foreach (var m in models)
                {
                    AvailableLocalModels.Add(m.Name);
                }
            }
            catch { /* Ignore */ }
        }

        [RelayCommand]
        private void SaveModeSettings()
        {
            _modeService.SaveModes();
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
            var config = _modeService.GetType()
                .GetField("_configService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(_modeService) as WhisperConfigurationService;
            
            if (config != null)
            {
                var currentConfig = config.CurrentConfiguration;
                config.SaveConfiguration(currentConfig);
            }
        }
    }
}
