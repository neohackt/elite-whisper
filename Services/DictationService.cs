using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EliteWhisper.Models;
using EliteWhisper.ViewModels;
using EliteWhisper.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace EliteWhisper.Services
{
    public enum RecordingSource
    {
        None,
        Widget,
        App
    }

    public class DictationService
    {
        private readonly AudioCaptureService _audioService;
        private readonly TextInjectionService _injectionService;
        private readonly AIEngineService _aiEngine;
        private readonly WidgetViewModel _widgetViewModel;
        private readonly HistoryService _historyService;
        private readonly PostProcessingService _postProcessingService;
        private readonly ModeService _modeService;
        private CancellationTokenSource? _cts;
        private string? _currentAudioPath;
        private int _retryCount = 0;
        private const int MAX_RETRIES = 2;
        private TimeSpan _recordingDuration = TimeSpan.Zero;
        private DateTime _recordingStartTime;

        public RecordingSource CurrentSource { get; private set; } = RecordingSource.None;

        public DictationService(
            AudioCaptureService audioService,
            TextInjectionService injectionService,
            AIEngineService aiEngine,
            WidgetViewModel widgetViewModel,
            HistoryService historyService,
            PostProcessingService postProcessingService,
            ModeService modeService)
        {
            _audioService = audioService;
            _injectionService = injectionService;
            _aiEngine = aiEngine;
            _widgetViewModel = widgetViewModel;
            _historyService = historyService;
            _postProcessingService = postProcessingService;
            _modeService = modeService;

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
        public void StartListening(RecordingSource source)
        {
            // Start check:
            // 1. If Widget is source (or intent): Widget must be Ready or Hidden
            // 2. If App is source: Engine must be Ready/Idle
            
            // Simpler global check: Engine must NOT be Recording/Processing
            if (_aiEngine.State == EngineState.Recording || _aiEngine.State == EngineState.Processing) return;

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

            // Set Source BEFORE triggering state change so listeners know who started it
            CurrentSource = source;

            // Sync with Engine State
            try 
            {
                _aiEngine.SetState(EngineState.Recording);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set engine state: {ex.Message}");
                // If we can't transition engine to recording (e.g. it's busy loading), we shouldn't start
                CurrentSource = RecordingSource.None; // Reset source
                return; 
            }

            _cts = new CancellationTokenSource();
            _currentAudioPath = Path.Combine(Path.GetTempPath(), $"elitewhisper_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            _retryCount = 0;
            
            if (CurrentSource == RecordingSource.Widget)
            {
                _widgetViewModel.State = WidgetState.Listening;
                _widgetViewModel.StatusText = "Listening...";
            }
            
            _recordingStartTime = DateTime.Now;
            _audioService.StartRecording(_currentAudioPath);
        }

        /// <summary>
        /// Called when F2 is pressed in Listening state.
        /// Transitions: Listening -> Processing
        /// </summary>
        public async Task StopListeningAndProcessAsync()
        {
            await Task.Yield(); // Ensure async execution context
            
            // Check based on source
            if (CurrentSource == RecordingSource.Widget)
            {
                if (_widgetViewModel.State != WidgetState.Listening) return;
            }
            else if (CurrentSource == RecordingSource.App)
            {
                if (_aiEngine.State != EngineState.Recording) return;
            }
            else
            {
                return; // No valid source
            }

            // Sync Engine State
            _aiEngine.SetState(EngineState.Processing);
            
            if (CurrentSource == RecordingSource.Widget)
            {
                _widgetViewModel.State = WidgetState.Processing;
                _widgetViewModel.StatusText = "Processing...";
            }
            
            _recordingDuration = DateTime.Now - _recordingStartTime;
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
            if (CurrentSource == RecordingSource.Widget)
            {
                _widgetViewModel.State = WidgetState.Ready;
                _widgetViewModel.StatusText = "Cancelled";
            }
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
                    if (CurrentSource == RecordingSource.Widget)
                    {
                        _widgetViewModel.StatusText = "Transcribing...";
                    }
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
                    // Post-process with LLM if enabled
                    var activeMode = _modeService.ActiveMode;
                    var finalText = await _postProcessingService.ProcessAsync(transcription, activeMode);
                    
                    if (CurrentSource == RecordingSource.Widget)
                    {
                        _widgetViewModel.StatusText = "Typing...";
                    }
                    await _injectionService.InjectTextAsync(finalText, _cts?.Token ?? CancellationToken.None);
                    
                    // Capture metrics
                    int wordCount = finalText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    
                    // Duration from audio file or approximated. 
                    
                    int durationSec = (int)_recordingDuration.TotalSeconds;
                    if (durationSec < 1) durationSec = 1;

                    // Capture Active Window
                    string activeWindow = "Unknown";
                    try
                    {
                        var handle = EliteWhisper.Native.Win32.GetForegroundWindow();
                        if (handle != IntPtr.Zero)
                        {
                            var sb = new System.Text.StringBuilder(256);
                            if (EliteWhisper.Native.Win32.GetWindowText(handle, sb, 256) > 0)
                            {
                                activeWindow = sb.ToString();
                            }
                        }
                    }
                    catch { }

                    // Save to History (save final text, not raw)
                    _historyService.AddRecord(new Models.DictationRecord
                    {
                        Content = finalText,
                        Timestamp = DateTime.Now,
                        Duration = _recordingDuration,
                        DurationSeconds = durationSec,
                        WordCount = wordCount,
                        ModelUsed = _aiEngine.GetConfiguration()?.DefaultModelPath ?? "Unknown",
                        ApplicationName = activeWindow
                    });
                    
                    // Notify Dashboard to update (could effectively be done via HistoryService event or Messenger)
                    CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Messages.RecordAddedMessage());

                    if (CurrentSource == RecordingSource.Widget)
                    {
                        _widgetViewModel.StatusText = "Done!";
                    }
                }
                else
                {
                    if (CurrentSource == RecordingSource.Widget)
                    {
                        _widgetViewModel.StatusText = "No speech detected";
                    }
                }
                
                await Task.Delay(800);
            }
            catch (OperationCanceledException)
            {
                if (CurrentSource == RecordingSource.Widget)
                {
                    _widgetViewModel.StatusText = "Cancelled";
                }
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
                if (CurrentSource == RecordingSource.Widget)
                {
                    _widgetViewModel.StatusText = "Timeout - try shorter audio";
                }
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
                    if (CurrentSource == RecordingSource.Widget)
                    {
                        _widgetViewModel.StatusText = $"Retrying... ({_retryCount}/{MAX_RETRIES})";
                    }
                    await Task.Delay(1000);
                    // Retry transcription
                    OnRecordingComplete(sender, audioFilePath);
                    return;
                }
                
                if (CurrentSource == RecordingSource.Widget)
                {
                    _widgetViewModel.StatusText = "Error - try again";
                }
                await Task.Delay(1500);
            }
            finally
            {
                // Cleanup audio file
                try { if (File.Exists(audioFilePath)) File.Delete(audioFilePath); } catch { }
                
                // Return to Ready (not Hidden) - only if still processing
                if (_widgetViewModel.State == WidgetState.Processing)
                {
                    if (CurrentSource == RecordingSource.Widget)
                    {
                        _widgetViewModel.State = WidgetState.Ready;
                        _widgetViewModel.StatusText = "Ready";
                    }
                }
                
                // Always ensure Engine returns to Ready
                if (_aiEngine.State == EngineState.Processing)
                {
                    _aiEngine.SetState(EngineState.Ready);
                }
                
                CurrentSource = RecordingSource.None;
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
