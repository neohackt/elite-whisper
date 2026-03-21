using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SherpaOnnx;

namespace EliteWhisper.Services.Speech
{
    /// <summary>
    /// Speech engine using Sherpa-ONNX for Parakeet TDT (Token-and-Duration Transducer) models.
    /// Accepts raw 16kHz PCM float audio — no mel spectrogram or FFT needed.
    /// CPU-first design, no CUDA dependency.
    /// </summary>
    public class SherpaOnnxEngine : ISpeechEngine, IDisposable
    {
        private OfflineRecognizer? _recognizer;
        private readonly string _modelDirectory;
        private readonly int _numThreads;
        private readonly string _engineName;
        private bool _disposed;

        public string Name => _engineName;
        public bool IsAvailable => _recognizer != null;

        public SherpaOnnxEngine(string modelDirectory, int? numThreads = null)
        {
            _modelDirectory = modelDirectory;
            _numThreads = numThreads ?? Math.Max(1, Environment.ProcessorCount / 2);
            _engineName = $"Parakeet (Sherpa)";

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // Validate model directory and required files
                if (!Directory.Exists(_modelDirectory))
                {
                    SttLogger.Log($"[STT] Sherpa model directory not found: {_modelDirectory}");
                    return;
                }

                // Parakeet TDT models use transducer architecture: encoder + decoder + joiner + tokens
                string encoderPath = FindModelFile("encoder*.onnx");
                string decoderPath = FindModelFile("decoder*.onnx");
                string joinerPath = FindModelFile("joiner*.onnx");
                string tokensPath = Path.Combine(_modelDirectory, "tokens.txt");

                if (string.IsNullOrEmpty(encoderPath) || string.IsNullOrEmpty(decoderPath) ||
                    string.IsNullOrEmpty(joinerPath) || !File.Exists(tokensPath))
                {
                    SttLogger.Log($"[STT] Sherpa model files incomplete in {_modelDirectory}. " +
                        $"encoder={!string.IsNullOrEmpty(encoderPath)}, decoder={!string.IsNullOrEmpty(decoderPath)}, " +
                        $"joiner={!string.IsNullOrEmpty(joinerPath)}, tokens={File.Exists(tokensPath)}");
                    return;
                }

                SttLogger.Log($"[STT] Sherpa initializing with encoder={Path.GetFileName(encoderPath)}, " +
                    $"decoder={Path.GetFileName(decoderPath)}, joiner={Path.GetFileName(joinerPath)}, " +
                    $"threads={_numThreads}");

                var config = new OfflineRecognizerConfig();

                // Feature extraction config — Sherpa handles mel internally
                config.FeatConfig.SampleRate = 16000;
                config.FeatConfig.FeatureDim = 80;

                // Transducer model config
                config.ModelConfig.Transducer.Encoder = encoderPath;
                config.ModelConfig.Transducer.Decoder = decoderPath;
                config.ModelConfig.Transducer.Joiner = joinerPath;
                config.ModelConfig.Tokens = tokensPath;
                config.ModelConfig.NumThreads = _numThreads;
                config.ModelConfig.Debug = 0;

                // Decoding
                config.DecodingMethod = "greedy_search";

                _recognizer = new OfflineRecognizer(config);

                SttLogger.Log($"[STT] Sherpa Parakeet engine initialized successfully. Model: {Path.GetFileName(_modelDirectory)}");
            }
            catch (Exception ex)
            {
                SttLogger.Log($"[STT] Sherpa initialization failed: {ex.Message}\n{ex.StackTrace}");
                _recognizer = null;
            }
        }

        public async Task<string> TranscribeAsync(float[] audioSamples, CancellationToken ct)
        {
            if (_recognizer == null)
                throw new InvalidOperationException("Sherpa Parakeet engine is not available or initialized.");

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                SttLogger.Log($"[STT] Using Sherpa Parakeet");
                SttLogger.Log($"[STT] Audio length: {audioSamples.Length} samples ({audioSamples.Length / 16000.0:F2}s)");

                var watch = Stopwatch.StartNew();

                try
                {
                    // Create stream and feed audio
                    var stream = _recognizer.CreateStream();
                    stream.AcceptWaveform(16000, audioSamples);

                    // Decode
                    _recognizer.Decode(stream);

                    // Get result
                    var result = stream.Result;
                    string text = result.Text?.Trim() ?? string.Empty;

                    watch.Stop();
                    SttLogger.Log($"[STT] Transcription time: {watch.ElapsedMilliseconds}ms");
                    SttLogger.Log($"[STT] Sherpa result: '{text}'");

                    return text;
                }
                catch (Exception ex)
                {
                    watch.Stop();
                    SttLogger.Log($"[STT] Sherpa transcription error after {watch.ElapsedMilliseconds}ms: {ex.Message}");
                    throw;
                }
            }, ct);
        }

        /// <summary>
        /// Finds a model file matching a glob pattern in the model directory.
        /// </summary>
        private string FindModelFile(string pattern)
        {
            try
            {
                var files = Directory.GetFiles(_modelDirectory, pattern, SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    return files[0];
            }
            catch { }
            return string.Empty;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _recognizer?.Dispose();
                _recognizer = null;
                _disposed = true;
            }
        }
    }
}
