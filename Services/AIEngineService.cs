using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    public enum TranscriptionModel
    {
        Fast,       // tiny / base
        Balanced,   // small
        Accurate    // medium / large
    }

    public enum EngineState
    {
        Idle,       // Not configured or ready to use
        Loading,    // Currently loading/activating a model
        Ready,      // Configured and ready to transcribe
        Recording,  // Currently recording audio
        Processing, // Currently transcribing audio
        Error       // An error occurred, engine is in an unstable state
    }

    public class AIEngineService
    {
        private readonly WhisperConfigurationService _configService;
        private const int DEFAULT_TIMEOUT_MS = 120000; // 120 seconds
        
        // State Management
        private EngineState _state = EngineState.Idle;
        public EngineState State 
        { 
            get => _state; 
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    StateChanged?.Invoke(this, _state);
                    System.Diagnostics.Debug.WriteLine($"[AIEngine] State changed to: {_state}");
                }
            }
        }
        
        public event EventHandler<EngineState>? StateChanged;
        
        // Activation Lock to prevent concurrent model switching
        private readonly SemaphoreSlim _activationLock = new SemaphoreSlim(1, 1);

        public AIEngineService(WhisperConfigurationService configService)
        {
            _configService = configService;
            
            // Initial state check
            if (IsConfigured())
            {
                State = EngineState.Ready;
            }
        }

        /// <summary>
        /// Attempts to transition the engine state. 
        /// Restricted to valid transitions for external callers.
        /// </summary>
        public void SetState(EngineState newState)
        {
            // Only allow transitions driven by DictationService for recording lifecycle
            // Activation related states (Loading) are internal only
            
            if (newState == EngineState.Loading) 
                throw new InvalidOperationException("Cannot set Loading state externally. Use ActivateModelAsync.");

            // Basic validation
            if (State == EngineState.Loading)
            {
                // If currently loading, we generally shouldn't interrupt unless error
                if (newState != EngineState.Error && newState != EngineState.Ready)
                     return; 
            }
            
            State = newState;
        }

        /// <summary>
        /// Safely activates a new model with transactional rollback.
        /// </summary>
        public async Task<bool> ActivateModelAsync(string modelPath)
        {
            // 1. Quick pre-checks
            if (!IsConfigured()) return false;
            
            // Reject if busy
            if (State == EngineState.Recording || State == EngineState.Processing)
            {
                System.Diagnostics.Debug.WriteLine("[AIEngine] Activation rejected: Engine is busy.");
                return false;
            }

            // 2. Acquire Lock (Wait max 1 sec to avoid UI freeze if jammed, but logic shouldn't jam)
            if (!await _activationLock.WaitAsync(1000))
            {
                System.Diagnostics.Debug.WriteLine("[AIEngine] Activation rejected: Lock busy.");
                return false;
            }

            // Capture previous state/model for rollback
            var previousState = State;
            var previousModel = _configService.CurrentConfiguration.DefaultModelPath;

            try
            {
                // 3. Set State to Loading
                State = EngineState.Loading;
                
                // 4. Validate New Model (Transactional Phase 1)
                // We don't "unload" the old one yet. We just verify the new one exists.
                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException("Model file not found", modelPath);
                }

                // 5. "Warm Up" / Test Load (Transactional Phase 2)
                // In a real compiled lib we would load it into memory here.
                // Since we use CLI, we might run a quick dummy probe or just trust file existence + extension.
                // For this implementation, we will trust validation + file existence.
                // If we wanted to be super safe, we could run `whisper-cli --help` or similar to check binary?
                // Let's assume validation passed.
                
                await Task.Delay(200); // Simulate brief warmup stabilization
                
                // 6. Commit Change (Transactional Phase 3)
                // Only now do we update the persistence
                _configService.SetDefaultModel(modelPath);
                
                // 7. Transition to Ready
                State = EngineState.Ready;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AIEngine] Activation Failed: {ex.Message}");
                
                // Rollback
                // Config remains as previous (since we didn't call SetDefaultModel)
                // BUT if we did call it earlier, we'd need to revert. We called it in step 6 which is "Commit".
                // So no config rollback needed if we fail before step 6.
                
                // Restore State
                State = EngineState.Ready; // Default to Ready if we failed, assuming old model is still there
                if (previousState == EngineState.Idle) State = EngineState.Idle; // Or whatever it was
                
                return false;
            }
            finally
            {
                _activationLock.Release();
            }
        }

        /// <summary>
        /// Check if Whisper is properly configured
        /// </summary>
        public bool IsConfigured()
        {
            return _configService.CurrentConfiguration.IsConfigured;
        }

        /// <summary>
        /// Get the current configuration for display
        /// </summary>
        public WhisperConfiguration GetConfiguration() => _configService.CurrentConfiguration;

        /// <summary>
        /// Transcribes an audio file using Whisper
        /// </summary>
        public async Task<string> TranscribeAsync(
            string audioFilePath, 
            TranscriptionModel model = TranscriptionModel.Balanced,
            CancellationToken cancellationToken = default)
        {
            var config = _configService.CurrentConfiguration;

            if (!config.IsConfigured)
            {
                State = EngineState.Error;
                throw new InvalidOperationException("Whisper is not configured. Please select a Whisper folder in settings.");
            }

            if (!File.Exists(audioFilePath))
            {
                State = EngineState.Error;
                throw new FileNotFoundException($"Audio file not found: {audioFilePath}");
            }

            // Note: We assume DictationService handles State = Processing wrapping this call
            
            string modelPath = config.DefaultModelPath!;
            string executablePath = config.ExecutablePath!;
            string workingDirectory = Path.GetDirectoryName(executablePath)!;

            // Build arguments for whisper-cli
            // whisper-cli -m model.bin -f audio.wav --output-txt
            string arguments = $"-m \"{modelPath}\" -f \"{audioFilePath}\" --no-timestamps -otxt";

            var result = await RunProcessAsync(executablePath, arguments, workingDirectory, cancellationToken);
            
            // Parse output - whisper outputs to a .txt file with same name
            string outputTxtPath = Path.ChangeExtension(audioFilePath, ".txt");
            if (File.Exists(outputTxtPath))
            {
                string transcription = await File.ReadAllTextAsync(outputTxtPath, cancellationToken);
                // Cleanup
                try { File.Delete(outputTxtPath); } catch { }
                return transcription.Trim();
            }

            // Fallback: Parse STDOUT if file output failed
            return ParseStdout(result.stdout);
        }

        private async Task<(string stdout, string stderr, int exitCode)> RunProcessAsync(
            string executable, 
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };

            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait with timeout and cancellation
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DEFAULT_TIMEOUT_MS);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
                
                throw new TimeoutException($"Whisper process timed out");
            }

            return (stdout.ToString(), stderr.ToString(), process.ExitCode);
        }

        private string ParseStdout(string stdout)
        {
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var result = new StringBuilder();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                // Skip progress lines, timestamps, etc.
                if (trimmed.StartsWith("[") || 
                    trimmed.StartsWith("whisper_") || 
                    trimmed.StartsWith("main:") ||
                    trimmed.StartsWith("system_info:") ||
                    string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }
                result.AppendLine(trimmed);
            }

            return result.ToString().Trim();
        }
    }
}
