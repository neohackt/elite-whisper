using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EliteWhisper.Models;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Services
{
    public class DictationService
    {
        private readonly AudioCaptureService _audioService;
        private readonly TextInjectionService _injectionService;
        private readonly AIEngineService _aiEngine;
        private readonly WidgetViewModel _widgetViewModel;
        private CancellationTokenSource? _cts;
        private string? _currentAudioPath;
        private int _retryCount = 0;
        private const int MAX_RETRIES = 2;

        public DictationService(
            AudioCaptureService audioService,
            TextInjectionService injectionService,
            AIEngineService aiEngine,
            WidgetViewModel widgetViewModel)
        {
            _audioService = audioService;
            _injectionService = injectionService;
            _aiEngine = aiEngine;
            _widgetViewModel = widgetViewModel;

            // Wire up visualization
            _audioService.AudioLevelUpdated += (s, level) => _widgetViewModel.UpdateMicLevel(level);
            _audioService.RecordingEncoded += OnRecordingComplete;
            _audioService.RecordingFailed += OnRecordingFailed;
            _audioService.MaxDurationReached += OnMaxDurationReached;
        }

        /// <summary>
        /// Called when F2 is pressed in Ready state.
        /// Transitions: Ready -> Listening
        /// </summary>
        public void StartListening()
        {
            if (_widgetViewModel.State != WidgetState.Ready) return;

            // Pre-flight checks
            if (!AudioCaptureService.IsMicrophoneAvailable())
            {
                ShowErrorAndReset("No microphone detected");
                return;
            }

            if (!_aiEngine.IsConfigured())
            {
                ShowErrorAndReset("Whisper not configured. Press S for Settings.");
                return;
            }

            // Sync with Engine State
            try 
            {
                _aiEngine.SetState(EngineState.Recording);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set engine state: {ex.Message}");
                // If we can't transition engine to recording (e.g. it's busy loading), we shouldn't start
                return; 
            }

            _cts = new CancellationTokenSource();
            _currentAudioPath = Path.Combine(Path.GetTempPath(), $"elitewhisper_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            _retryCount = 0;
            
            _widgetViewModel.State = WidgetState.Listening;
            _widgetViewModel.StatusText = "Listening...";
            
            _audioService.StartRecording(_currentAudioPath);
        }

        /// <summary>
        /// Called when F2 is pressed in Listening state.
        /// Transitions: Listening -> Processing
        /// </summary>
        public async Task StopListeningAndProcessAsync()
        {
            await Task.Yield(); // Ensure async execution context
            if (_widgetViewModel.State != WidgetState.Listening) return;

            // Sync Engine State
            _aiEngine.SetState(EngineState.Processing);
            
            _widgetViewModel.State = WidgetState.Processing;
            _widgetViewModel.StatusText = "Processing...";
            
            _audioService.StopRecording();
            // Recording completion triggers OnRecordingComplete callback
        }

        /// <summary>
        /// Cancel any ongoing operation
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();
            _audioService.StopRecording();
            _widgetViewModel.State = WidgetState.Ready;
            _widgetViewModel.StatusText = "Cancelled";
            // Restore engine state
            _aiEngine.SetState(EngineState.Ready);
        }

        private void OnMaxDurationReached(object? sender, EventArgs e)
        {
            // Auto-stop when max duration reached
            Application.Current.Dispatcher.Invoke(() =>
            {
                _widgetViewModel.StatusText = "Max duration - processing...";
            });
        }

        private async void OnRecordingComplete(object? sender, string audioFilePath)
        {
            try
            {
                string transcription;
                
                // Check if AI engine is configured
                if (_aiEngine.IsConfigured())
                {
                    _widgetViewModel.StatusText = "Transcribing...";
                    transcription = await _aiEngine.TranscribeAsync(
                        audioFilePath, 
                        TranscriptionModel.Balanced, 
                        _cts?.Token ?? CancellationToken.None);
                }
                else
                {
                    ShowErrorAndReset("Whisper not configured");
                    return;
                }
                
                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    _widgetViewModel.StatusText = "Typing...";
                    await _injectionService.InjectTextAsync(transcription, _cts?.Token ?? CancellationToken.None);
                    _widgetViewModel.StatusText = "Done!";
                }
                else
                {
                    _widgetViewModel.StatusText = "No speech detected";
                }
                
                await Task.Delay(800);
            }
            catch (OperationCanceledException)
            {
                _widgetViewModel.StatusText = "Cancelled";
                await Task.Delay(500);
            }
            catch (FileNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI Engine Error: {ex.Message}");
                ShowErrorAndReset("Whisper files missing");
                return;
            }
            catch (TimeoutException)
            {
                _widgetViewModel.StatusText = "Timeout - try shorter audio";
                await Task.Delay(1500);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
            {
                ShowErrorAndReset("Configure Whisper first (S)");
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dictation Error: {ex}");
                
                // Retry logic for transient failures
                if (_retryCount < MAX_RETRIES && !_cts?.Token.IsCancellationRequested == true)
                {
                    _retryCount++;
                    _widgetViewModel.StatusText = $"Retrying... ({_retryCount}/{MAX_RETRIES})";
                    await Task.Delay(1000);
                    // Retry transcription
                    OnRecordingComplete(sender, audioFilePath);
                    return;
                }
                
                _widgetViewModel.StatusText = "Error - try again";
                await Task.Delay(1500);
            }
            finally
            {
                // Cleanup audio file
                try { if (File.Exists(audioFilePath)) File.Delete(audioFilePath); } catch { }
                
                // Return to Ready (not Hidden) - only if still processing
                if (_widgetViewModel.State == WidgetState.Processing)
                {
                    _widgetViewModel.State = WidgetState.Ready;
                    _widgetViewModel.StatusText = "Ready";
                }
            }
        }

        private void OnRecordingFailed(object? sender, Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string errorMessage = ex.Message switch
                {
                    var m when m.Contains("microphone") => "No microphone",
                    var m when m.Contains("access denied") => "Mic access denied",
                    var m when m.Contains("too short") => "Recording too short",
                    _ => "Mic error"
                };
                
                System.Diagnostics.Debug.WriteLine($"Recording failed: {ex}");
                ShowErrorAndReset(errorMessage);
            });
        }

        private void FinishProcessing()
        {
            _widgetViewModel.State = WidgetState.Ready;
            _widgetViewModel.StatusText = "Ready";
            _aiEngine.SetState(EngineState.Ready);
        }

        private void ShowErrorAndReset(string message)
        {
            _widgetViewModel.State = WidgetState.Ready;
            _widgetViewModel.StatusText = "Error";
            _aiEngine.SetState(EngineState.Ready);
            
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
