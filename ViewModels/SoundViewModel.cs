using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Services;
using System;
using System.Windows;
using System.Windows.Threading;

namespace EliteWhisper.ViewModels
{
    public partial class SoundViewModel : ObservableObject, IDisposable
    {
        private readonly AudioCaptureService _audioCaptureService;
        private readonly Dispatcher _dispatcher;
        private bool _isMonitoring;

        [ObservableProperty]
        private double _inputLevel;

        [ObservableProperty]
        private string _inputLevelStatus = "Silent";

        [ObservableProperty]
        private string _inputLevelColor = "#9CA3AF"; // Gray/TextSecondary

        public SoundViewModel(AudioCaptureService audioCaptureService)
        {
            _audioCaptureService = audioCaptureService;
            _dispatcher = Application.Current.Dispatcher;
            
            // Subscribe to audio level updates
            _audioCaptureService.AudioLevelUpdated += OnAudioLevelUpdated;
        }

        [RelayCommand]
        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _audioCaptureService.StartMonitoring();
        }

        [RelayCommand]
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;
            _audioCaptureService.StopMonitoring();
            InputLevel = 0;
            InputLevelStatus = "Silent";
            InputLevelColor = "#9CA3AF";
        }

        private void OnAudioLevelUpdated(object? sender, float level)
        {
            if (!_isMonitoring) return;

            _dispatcher.Invoke(() =>
            {
                // Smooth the level a bit or just set it directly
                // Level is 0-1
                InputLevel = level * 100; // Convert to 0-100 for ProgressBar

                // Update Status Text and Color based on level
                if (InputLevel < 1)
                {
                    InputLevelStatus = "Silent";
                    InputLevelColor = "#9CA3AF"; // Gray
                }
                else if (InputLevel < 10)
                {
                    InputLevelStatus = "Low";
                    InputLevelColor = "#F59E0B"; // Amber/Warning
                }
                else if (InputLevel < 80)
                {
                    InputLevelStatus = "Good";
                    InputLevelColor = "#10B981"; // Green/Success
                }
                else
                {
                    InputLevelStatus = "Too Loud"; // Clipping potential
                    InputLevelColor = "#EF4444"; // Red/Error
                }
            });
        }

        public void Dispose()
        {
            StopMonitoring();
            _audioCaptureService.AudioLevelUpdated -= OnAudioLevelUpdated;
        }
    }
}
