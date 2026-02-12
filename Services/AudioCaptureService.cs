using NAudio.Wave;
using System;
using System.IO;
using System.Timers;

namespace EliteWhisper.Services
{
    public class AudioCaptureService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private string? _currentFilePath;
        private readonly object _lockObject = new object();
        private bool _isRecording;
        private System.Timers.Timer? _maxDurationTimer;

        // Configuration
        public int MaxRecordingDurationSeconds { get; set; } = 300; // 5 minutes max
        public int MinRecordingDurationMs { get; set; } = 500; // Minimum 0.5 seconds
        public int DeviceNumber { get; set; } = 0; // Default to 0

        // Events
        public event EventHandler<float>? AudioLevelUpdated;
        public event EventHandler<string>? RecordingEncoded;
        public event EventHandler<Exception>? RecordingFailed;
        public event EventHandler? MaxDurationReached;

        public bool IsRecording => _isRecording;
        public DateTime? RecordingStartTime { get; private set; }

        /// <summary>
        /// Check if any microphone is available
        /// </summary>
        public static bool IsMicrophoneAvailable()
        {
            try
            {
                return WaveIn.DeviceCount > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the name of the default microphone (Device 0)
        /// </summary>
        public string DefaultMicName 
        {
            get
            {
                try
                {
                    if (WaveIn.DeviceCount > 0)
                    {
                        var caps = WaveIn.GetCapabilities(DeviceNumber);
                        return caps.ProductName;
                    }
                    return "No Microphone Found";
                }
                catch
                {
                    return "Default Microphone";
                }
            }
        }

        /// <summary>
        /// Get list of available microphones
        /// </summary>
        public static string[] GetAvailableMicrophones()
        {
            var result = new string[WaveIn.DeviceCount];
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                result[i] = caps.ProductName;
            }
            return result;
        }

        public void StartRecording(string filePath)
        {
            lock (_lockObject)
            {
                if (_isRecording) return;

                // Check mic availability
                if (!IsMicrophoneAvailable())
                {
                    RecordingFailed?.Invoke(this, new InvalidOperationException("No microphone detected. Please connect a microphone."));
                    return;
                }
                
                try
                {
                    _currentFilePath = filePath;
                    
                    // Cleanup previous instance if dirty
                    CleanupResources();

                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = DeviceNumber,
                        WaveFormat = new WaveFormat(16000, 16, 1), // Whisper standard: 16kHz, 16-bit, Mono
                        BufferMilliseconds = 20
                    };

                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.RecordingStopped += OnRecordingStopped;

                    _writer = new WaveFileWriter(filePath, _waveIn.WaveFormat);

                    // Start max duration timer
                    _maxDurationTimer = new System.Timers.Timer(MaxRecordingDurationSeconds * 1000);
                    _maxDurationTimer.Elapsed += OnMaxDurationReached;
                    _maxDurationTimer.AutoReset = false;
                    _maxDurationTimer.Start();

                    _waveIn.StartRecording();
                    _isRecording = true;
                    RecordingStartTime = DateTime.Now;
                }
                catch (NAudio.MmException mmEx)
                {
                    CleanupResources();
                    RecordingFailed?.Invoke(this, new InvalidOperationException($"Microphone access denied or unavailable: {mmEx.Message}"));
                }
                catch (Exception ex)
                {
                    CleanupResources();
                    RecordingFailed?.Invoke(this, ex);
                }
            }
        }

        public void StartMonitoring()
        {
            lock (_lockObject)
            {
                if (_isRecording || _waveIn != null) return;

                if (!IsMicrophoneAvailable()) return;

                try
                {
                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = DeviceNumber,
                        WaveFormat = new WaveFormat(16000, 16, 1),
                        BufferMilliseconds = 20
                    };

                    _waveIn.DataAvailable += OnDataAvailable;
                    // No writer, no timer, just monitoring
                    
                    _waveIn.StartRecording();
                    // We don't set _isRecording = true because that implies saving to file in other logic
                    // But we might need a flag _isMonitoring to distinguish? 
                    // For now, let's rely on _writer being null to know we are not saving.
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start monitoring: {ex.Message}");
                }
            }
        }

        public void StopMonitoring()
        {
            lock (_lockObject)
            {
                if (_isRecording) return; // Don't interrupt actual recording
                
                if (_waveIn != null)
                {
                    try
                    {
                        _waveIn.StopRecording();
                    }
                    catch { }
                    CleanupResources();
                }
            }
        }

        private void OnMaxDurationReached(object? sender, ElapsedEventArgs e)
        {
            MaxDurationReached?.Invoke(this, EventArgs.Empty);
            StopRecording();
        }

        public void StopRecording()
        {
            lock (_lockObject)
            {
                if (!_isRecording || _waveIn == null) return;

                // Stop the max duration timer
                _maxDurationTimer?.Stop();
                _maxDurationTimer?.Dispose();
                _maxDurationTimer = null;

                try 
                {
                    // Check minimum duration
                    var duration = DateTime.Now - (RecordingStartTime ?? DateTime.Now);
                    if (duration.TotalMilliseconds < MinRecordingDurationMs)
                    {
                        System.Diagnostics.Debug.WriteLine($"Recording too short ({duration.TotalMilliseconds}ms), discarding.");
                    }

                    // StopRecording invokes OnRecordingStopped asynchronously
                    _waveIn.StopRecording();
                }
                catch (Exception ex)
                {
                    RecordingFailed?.Invoke(this, ex);
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
             // Write to file
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);

            // Calculate peak level for visualization
            // 16-bit PCM = 2 bytes per sample
            float max = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                // BitConverter logic manually for speed
                short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i + 0]);
                var sample32 = Math.Abs(sample / 32768f);
                if (sample32 > max) max = sample32;
            }

            AudioLevelUpdated?.Invoke(this, max);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            string? filePath;
            
            lock (_lockObject)
            {
                _isRecording = false;
                filePath = _currentFilePath;
                CleanupResources();
            }

            if (e.Exception != null)
            {
                RecordingFailed?.Invoke(this, e.Exception);
            }
            else if (filePath != null && File.Exists(filePath))
            {
                // Check file size (empty or corrupt)
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 1000) // Less than 1KB = likely empty
                {
                    RecordingFailed?.Invoke(this, new InvalidOperationException("Recording too short or no audio captured."));
                    try { File.Delete(filePath); } catch { }
                }
                else
                {
                    RecordingEncoded?.Invoke(this, filePath);
                }
            }
        }

        private void CleanupResources()
        {
            _writer?.Dispose();
            _writer = null;

            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }

            _maxDurationTimer?.Dispose();
            _maxDurationTimer = null;
            RecordingStartTime = null;
        }

        public void Dispose()
        {
            StopRecording();
        }
    }
}
