using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace EliteWhisper.Services.Speech
{
    public class SpeechRecognitionService
    {
        private readonly SpeechEngineSelector _engineSelector;

        public SpeechRecognitionService(SpeechEngineSelector engineSelector)
        {
            _engineSelector = engineSelector;
        }

        public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct)
        {
            // First, load the audio file into float samples
            // We use standard NAudio MediaFoundationReader to resample to 16kHz mono
            float[] audioSamples = await LoadAudioSamplesAsync(audioFilePath, 16000, ct);

            // 5. Improve Silence Detection
            double sumSquares = 0;
            for (int i = 0; i < audioSamples.Length; i++)
                sumSquares += audioSamples[i] * audioSamples[i];
            double audioRms = Math.Sqrt(sumSquares / Math.Max(1, audioSamples.Length));

            // 6. Add Debug Logging
            EliteWhisper.Services.Speech.SttLogger.Log($"[STT] Input audio length: {audioSamples.Length} samples, RMS: {audioRms:F4}");

            var engine = _engineSelector.GetBestEngine();

            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string transcript = await engine.TranscribeAsync(audioSamples, ct);
                watch.Stop();

                EliteWhisper.Services.Speech.SttLogger.Log($"[STT] ONNX inference duration: {watch.ElapsedMilliseconds}ms for engine {engine.Name}");
                EliteWhisper.Services.Speech.SttLogger.Log($"[STT] Decoded transcript length: {transcript.Length}");
                EliteWhisper.Services.Speech.SttLogger.Log($"[STT] Final transcript: '{transcript}'");

                // 7. Fallback to Whisper
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    if (audioRms < 0.01)
                    {
                        EliteWhisper.Services.Speech.SttLogger.Log("[STT] audioRMS < 0.01 and empty transcript, confirming 'No speech detected'.");
                        return "";
                    }

                    var fallback = _engineSelector.GetFallbackEngine();
                    if (fallback != engine && fallback.IsAvailable)
                    {
                        EliteWhisper.Services.Speech.SttLogger.Log($"[STT] Engine returned empty but audio is loud. Falling back to {fallback.Name}");
                        transcript = await fallback.TranscribeAsync(audioSamples, ct);
                    }
                }

                return transcript;
            }
            catch (Exception ex)
            {
                EliteWhisper.Services.Speech.SttLogger.Log($"[STT] Primary engine {engine.Name} failed: {ex.Message}");
                
                // Fallback Safety Process
                var fallback = _engineSelector.GetFallbackEngine();
                if (fallback != engine && fallback.IsAvailable)
                {
                    EliteWhisper.Services.Speech.SttLogger.Log($"[STT] Error occurred. Falling back to {fallback.Name}");
                    return await fallback.TranscribeAsync(audioSamples, ct);
                }
                
                throw;
            }
        }

        private async Task<float[]> LoadAudioSamplesAsync(string filePath, int targetSampleRate, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                using var reader = new MediaFoundationReader(filePath);
                ISampleProvider provider = reader.ToSampleProvider();

                // 1. Audio Format Requirements (16kHz, mono)
                if (provider.WaveFormat.SampleRate != targetSampleRate)
                {
                    provider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(provider, targetSampleRate);
                }

                if (provider.WaveFormat.Channels > 1)
                {
                    provider = new NAudio.Wave.SampleProviders.MultiplexingSampleProvider(new[] { provider }, 1);
                }
                
                // Read all samples
                long projectedLength = (long)(reader.TotalTime.TotalSeconds * targetSampleRate);
                var sampleList = new System.Collections.Generic.List<float>((int)projectedLength);
                
                float[] buffer = new float[16000];
                int read;
                while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    for(int i = 0; i < read; i++)
                    {
                        // ensure [-1, 1] bounds just in case
                        float s = buffer[i];
                        if (s > 1f) s = 1f;
                        if (s < -1f) s = -1f;
                        sampleList.Add(s);
                    }
                }

                return sampleList.ToArray();
            }, ct);
        }
    }
}
