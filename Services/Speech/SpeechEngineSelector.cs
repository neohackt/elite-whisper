using System;
using System.IO;
using System.Linq;

namespace EliteWhisper.Services.Speech
{
    public class SpeechEngineSelector
    {
        private readonly HardwareDetectionService _hardwareProfile;
        private readonly WhisperConfigurationService _configService;
        
        private SherpaOnnxEngine? _sherpaEngine;
        private WhisperEngine _whisper;
        
        public SpeechEngineSelector(
            HardwareDetectionService hardwareProfile, 
            WhisperConfigurationService configService,
            AIEngineService whisperCore)
        {
            _hardwareProfile = hardwareProfile;
            _configService = configService;
            
            _whisper = new WhisperEngine(whisperCore);
            
            InitializeEngines();
        }

        private void InitializeEngines()
        {
            var config = _configService.CurrentConfiguration;
            string appDataPath = config.ModelsDirectory ?? Path.Combine(config.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "models");
            SttLogger.Log($"[STT] Selector analyzing ModelsDirectory: {appDataPath}");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            // ── Sherpa-ONNX Parakeet TDT ──────────────────────────────────
            InitializeSherpaEngine(appDataPath);
        }

        private void InitializeSherpaEngine(string modelsDir)
        {
            // Look for known Sherpa model directories
            string[] sherpaModelNames = new[]
            {
                "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
                "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
                "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2",
                "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3",
            };

            foreach (var name in sherpaModelNames)
            {
                string modelDir = Path.Combine(modelsDir, name);
                if (Directory.Exists(modelDir))
                {
                    // Verify it has the required files
                    string tokensPath = Path.Combine(modelDir, "tokens.txt");
                    bool hasEncoder = Directory.GetFiles(modelDir, "encoder*.onnx").Length > 0;
                    bool hasDecoder = Directory.GetFiles(modelDir, "decoder*.onnx").Length > 0;
                    bool hasJoiner = Directory.GetFiles(modelDir, "joiner*.onnx").Length > 0;
                    bool hasTokens = File.Exists(tokensPath);

                    if (hasEncoder && hasDecoder && hasJoiner && hasTokens)
                    {
                        SttLogger.Log($"[STT] Discovered Sherpa Parakeet TDT model at: {modelDir}");
                        try
                        {
                            _sherpaEngine = new SherpaOnnxEngine(modelDir);
                            if (_sherpaEngine.IsAvailable)
                            {
                                SttLogger.Log("[STT] Sherpa Parakeet engine initialized successfully.");
                                return; // Use the first valid model
                            }
                            else
                            {
                                SttLogger.Log("[STT] Sherpa engine created but not available — disposing.");
                                _sherpaEngine.Dispose();
                                _sherpaEngine = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            SttLogger.Log($"[STT] Sherpa engine initialization failed: {ex.Message}");
                            _sherpaEngine = null;
                        }
                    }
                    else
                    {
                        SttLogger.Log($"[STT] Sherpa model directory {name} is incomplete.");
                    }
                }
            }

            // Also scan for any directory containing the transducer files
            try
            {
                foreach (var dir in Directory.GetDirectories(modelsDir))
                {
                    if (_sherpaEngine != null) break; // Already found one

                    string dirName = Path.GetFileName(dir);
                    if (!dirName.StartsWith("sherpa-onnx", StringComparison.OrdinalIgnoreCase)) continue;

                    string tokensPath = Path.Combine(dir, "tokens.txt");
                    bool hasEncoder = Directory.GetFiles(dir, "encoder*.onnx").Length > 0;
                    bool hasDecoder = Directory.GetFiles(dir, "decoder*.onnx").Length > 0;
                    bool hasJoiner = Directory.GetFiles(dir, "joiner*.onnx").Length > 0;

                    if (hasEncoder && hasDecoder && hasJoiner && File.Exists(tokensPath))
                    {
                        SttLogger.Log($"[STT] Discovered generic Sherpa model at: {dir}");
                        try
                        {
                            _sherpaEngine = new SherpaOnnxEngine(dir);
                            if (_sherpaEngine.IsAvailable)
                            {
                                SttLogger.Log($"[STT] Sherpa engine loaded from {dirName}");
                                return;
                            }
                            else
                            {
                                _sherpaEngine.Dispose();
                                _sherpaEngine = null;
                            }
                        }
                        catch
                        {
                            _sherpaEngine = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SttLogger.Log($"[STT] Error scanning for Sherpa models: {ex.Message}");
            }

            if (_sherpaEngine == null)
            {
                SttLogger.Log("[STT] No Sherpa Parakeet TDT model found. Download one from the Models page.");
            }
        }

        public ISpeechEngine GetBestEngine()
        {
            var config = _configService.CurrentConfiguration;
            var profile = _hardwareProfile.GetProfile();

            SttLogger.Log($"[STT] Selector running. HasNvidiaGpu: {profile.HasNvidiaGpu}, CpuCores: {profile.CpuCores}, " +
                $"AutoSelect: {config.AutoSelectSTT}, Preferred: {config.PreferredSTTEngine}, " +
                $"SherpaAvailable: {_sherpaEngine?.IsAvailable == true}");

            // 1. Check User Override
            if (!config.AutoSelectSTT && !string.IsNullOrEmpty(config.PreferredSTTEngine) && config.PreferredSTTEngine != "Auto")
            {
                if (config.PreferredSTTEngine == "Sherpa" && _sherpaEngine?.IsAvailable == true)
                {
                    SttLogger.Log("[STT] Selected engine (Manual): Sherpa Parakeet");
                    return _sherpaEngine;
                }
                if (config.PreferredSTTEngine == "Whisper" && _whisper.IsAvailable)
                {
                    SttLogger.Log("[STT] Selected engine (Manual): Whisper");
                    return _whisper;
                }
            }

            // 2. Auto Select: Sherpa first (CPU-friendly, no GPU issues), then Whisper
            if (_sherpaEngine != null && _sherpaEngine.IsAvailable)
            {
                SttLogger.Log("[STT] Selected engine (Auto): Sherpa Parakeet TDT");
                return _sherpaEngine;
            }

            // 3. Fallback
            SttLogger.Log("[STT] Selected engine (Fallback): Whisper");
            return _whisper;
        }

        public ISpeechEngine GetFallbackEngine()
        {
            return _whisper;
        }
    }
}
