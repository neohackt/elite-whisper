using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace EliteWhisper.ViewModels
{
    public partial class SoundViewModel : ObservableObject, IDisposable
    {
        private readonly AudioCaptureService _audioCaptureService;
        private readonly AudioPlayerService _audioPlayerService;
        private readonly Dispatcher _dispatcher;
        private string? _tempTestFilePath;

        [ObservableProperty]
        private bool _isMonitoring;

        [ObservableProperty]
        private string _monitoringButtonText = "Start Monitoring";

        [ObservableProperty]
        private double _inputLevel;

        [ObservableProperty]
        private string _inputLevelStatus = "Silent";

        [ObservableProperty]
        private string _inputLevelColor = "#9CA3AF"; // Gray/TextSecondary

        // Microphone Selection
        [ObservableProperty]
        private ObservableCollection<string> _availableMicrophones = new();

        [ObservableProperty]
        private int _selectedMicrophoneIndex = 0;

        // Test Recording Properties
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TestButtonText))]
        private bool _isTestingRecording;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        private bool _isTestingPlaying;

        [ObservableProperty]
        private bool _canPlayTest;

        public string TestButtonText => IsTestingRecording ? "Stop Test" : "Start Test";
        public string PlayButtonText => IsTestingPlaying ? "Stop Playing" : "Play Recording";

        public SoundViewModel(AudioCaptureService audioCaptureService, AudioPlayerService audioPlayerService)
        {
            _audioCaptureService = audioCaptureService;
            _audioPlayerService = audioPlayerService;
            _dispatcher = Application.Current.Dispatcher;
            
            _audioCaptureService.AudioLevelUpdated += OnAudioLevelUpdated;
            _audioPlayerService.PlaybackStopped += OnPlaybackStopped;

            RefreshDevices();
        }

        partial void OnIsMonitoringChanged(bool value)
        {
            MonitoringButtonText = value ? "Stop Monitoring" : "Start Monitoring";
            if (value)
            {
                // Ensure other modes are off
                if (IsTestingRecording) IsTestingRecording = false;
                if (IsTestingPlaying) IsTestingPlaying = false;
                
                _audioCaptureService.StartMonitoring();
            }
            else
            {
                _audioCaptureService.StopMonitoring();
                ResetLevel();
            }
        }

        partial void OnIsTestingRecordingChanged(bool value)
        {
            if (value)
            {
                // Start Recording
                if (IsMonitoring) IsMonitoring = false;
                if (IsTestingPlaying) IsTestingPlaying = false;

                try
                {
                    _tempTestFilePath = Path.GetTempFileName().Replace(".tmp", ".wav");
                    _audioCaptureService.StartRecording(_tempTestFilePath);
                    CanPlayTest = false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to start recording: {ex.Message}");
                    IsTestingRecording = false; // Revert
                }
            }
            else
            {
                // Stop Recording
                _audioCaptureService.StopRecording();
                CanPlayTest = true;
            }
        }

        partial void OnIsTestingPlayingChanged(bool value)
        {
            if (value)
            {
                // Start Playback
                if (IsTestingRecording) IsTestingRecording = false;
                if (IsMonitoring) IsMonitoring = false;

                if (_tempTestFilePath == null || !File.Exists(_tempTestFilePath))
                {
                    IsTestingPlaying = false;
                    return;
                }

                try
                {
                    _audioPlayerService.Play(_tempTestFilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to play recording: {ex.Message}");
                    IsTestingPlaying = false;
                }
            }
            else
            {
                // Stop Playback
                _audioPlayerService.Stop();
            }
        }

        partial void OnSelectedMicrophoneIndexChanged(int value)
        {
            if (value >= 0)
            {
                // If monitoring, restart to apply change
                bool wasMonitoring = IsMonitoring;
                if (wasMonitoring) IsMonitoring = false;

                _audioCaptureService.DeviceNumber = value;
                
                if (wasMonitoring) IsMonitoring = true;
            }
        }

        [RelayCommand]
        public void RefreshDevices()
        {
            AvailableMicrophones.Clear();
            var mics = AudioCaptureService.GetAvailableMicrophones();
            if (mics.Length == 0)
            {
                AvailableMicrophones.Add("No Microphone Found");
                SelectedMicrophoneIndex = -1;
            }
            else
            {
                foreach (var mic in mics)
                {
                    AvailableMicrophones.Add(mic);
                }
                // Validate index
                if (_audioCaptureService.DeviceNumber < mics.Length)
                {
                    SelectedMicrophoneIndex = _audioCaptureService.DeviceNumber;
                }
                else
                {
                    SelectedMicrophoneIndex = 0;
                }
            }
        }

        [RelayCommand]
        public void ToggleMonitoring() => IsMonitoring = !IsMonitoring;

        [RelayCommand]
        public void ToggleTestRecording() => IsTestingRecording = !IsTestingRecording;

        [RelayCommand]
        public void ToggleTestPlayback() => IsTestingPlaying = !IsTestingPlaying;

        private void OnPlaybackStopped(object? sender, EventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                IsTestingPlaying = false; // Will trigger OnIsTestingPlayingChanged(false) -> Stop() -> benign
                CanPlayTest = false; // Disable button
                
                // Delete file
                if (_tempTestFilePath != null && File.Exists(_tempTestFilePath))
                {
                    try
                    {
                        File.Delete(_tempTestFilePath);
                        _tempTestFilePath = null;
                    }
                    catch { }
                }
            });
        }

        private void OnAudioLevelUpdated(object? sender, float level)
        {
            if (!IsMonitoring && !IsTestingRecording) return;

            _dispatcher.Invoke(() =>
            {
                InputLevel = level * 100;

                if (InputLevel < 1)
                {
                    InputLevelStatus = "Silent";
                    InputLevelColor = "#9CA3AF";
                }
                else if (InputLevel < 10)
                {
                    InputLevelStatus = "Low";
                    InputLevelColor = "#F59E0B";
                }
                else if (InputLevel < 80)
                {
                    InputLevelStatus = "Good";
                    InputLevelColor = "#10B981";
                }
                else
                {
                    InputLevelStatus = "Too Loud";
                    InputLevelColor = "#EF4444";
                }
            });
        }

        private void ResetLevel()
        {
            InputLevel = 0;
            InputLevelStatus = "Silent";
            InputLevelColor = "#9CA3AF";
        }

        public void Dispose()
        {
            IsMonitoring = false;
            IsTestingRecording = false;
            IsTestingPlaying = false;

            _audioCaptureService.AudioLevelUpdated -= OnAudioLevelUpdated;
            _audioPlayerService.PlaybackStopped -= OnPlaybackStopped;

            // Cleanup potential temp file
            if (_tempTestFilePath != null && File.Exists(_tempTestFilePath))
            {
                try { File.Delete(_tempTestFilePath); } catch { }
            }
        }
    }
}
