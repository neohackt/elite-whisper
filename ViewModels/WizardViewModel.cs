using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Services;
using System;
using System.IO;
using System.Windows;

namespace EliteWhisper.ViewModels
{
    public partial class WizardViewModel : ObservableObject
    {
        private readonly WhisperConfigurationService _configService;
        private readonly Action _onFinish;

        [ObservableProperty]
        private int _currentStepIndex = 0;

        [ObservableProperty]
        private string _modelsDirectory;
        
        // UI Properties relating to steps
        public bool IsStep1 => CurrentStepIndex == 0;
        public bool IsStep2 => CurrentStepIndex == 1;
        public bool IsStep3 => CurrentStepIndex == 2;
        public bool IsStep4 => CurrentStepIndex == 3;

        public string FinishButtonText => IsStep4 ? "Finish" : "Next";

        public WizardViewModel(WhisperConfigurationService configService, Action onFinish)
        {
            _configService = configService;
            _onFinish = onFinish;

            var config = _configService.CurrentConfiguration;
            // Initialize with current or default
            _modelsDirectory = !string.IsNullOrEmpty(config.ModelsDirectory) 
                ? config.ModelsDirectory 
                : Path.Combine(config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "models");
        }

        [RelayCommand]
        private void Next()
        {
            if (IsStep4)
            {
                Finish();
            }
            else
            {
                CurrentStepIndex++;
                NotifyStepProperties();
            }
        }

        [RelayCommand]
        private void ChangeDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Model Storage Location",
                InitialDirectory = ModelsDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                ModelsDirectory = dialog.FolderName;
            }
        }

        private void Finish()
        {
            try
            {
                // 1. Update Config Object
                var config = _configService.CurrentConfiguration;
                config.ModelsDirectory = ModelsDirectory;
                config.HasCompletedFirstRun = true;

                // 2. Persist
                _configService.ApplyConfiguration(
                    config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, 
                    _configService.DiscoverAndValidate(config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory));
                
                // 3. Callback to close
                _onFinish?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NotifyStepProperties()
        {
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(IsStep4));
            OnPropertyChanged(nameof(FinishButtonText));
        }
    }
}
