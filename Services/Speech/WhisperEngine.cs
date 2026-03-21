using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EliteWhisper.Services.Speech
{
    /// <summary>
    /// Wrapper around the existing AIEngineService (Whisper CLI) to implement ISpeechEngine.
    /// Note: Whisper CLI currently takes a file instead of raw audio samples, so we will
    /// write the samples to a temp WAV file, transcribe, and then clean up.
    /// </summary>
    public class WhisperEngine : ISpeechEngine
    {
        private readonly AIEngineService _aiEngineService;
        
        public WhisperEngine(AIEngineService aiEngineService)
        {
            _aiEngineService = aiEngineService;
        }

        public string Name => "Whisper";

        public bool IsAvailable => _aiEngineService.IsConfigured();

        public async Task<string> TranscribeAsync(float[] audioSamples, CancellationToken ct)
        {
            // The existing AIEngineService expects a WAV file path.
            // We need to convert the float array to a WAV file temporarily.
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"whisper_temp_{Guid.NewGuid():N}.wav");
            
            try
            {
                SaveSamplesAsWav(audioSamples, 16000, tempFilePath); // Whisper uses 16kHz
                
                // Use the Balanced model as default for dictation, or fetch from config
                string result = await _aiEngineService.TranscribeAsync(tempFilePath, TranscriptionModel.Balanced, ct);
                return result;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        private void SaveSamplesAsWav(float[] samples, int sampleRate, string filePath)
        {
            var format = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            using (var writer = new NAudio.Wave.WaveFileWriter(filePath, format))
            {
                writer.WriteSamples(samples, 0, samples.Length);
            }
        }
    }
}
