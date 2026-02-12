using NAudio.Wave;
using System;
using System.IO;

namespace EliteWhisper.Services
{
    public class AudioPlayerService : IDisposable
    {
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioFileReader;
        
        public event EventHandler? PlaybackStopped;

        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

        public void Play(string filePath)
        {
            Stop(); // Stop any current playback

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Audio file not found.", filePath);
            }

            try
            {
                _waveOut = new WaveOutEvent();
                _audioFileReader = new AudioFileReader(filePath);
                
                _waveOut.Init(_audioFileReader);
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                CleanupResources();
                throw new InvalidOperationException($"Failed to play audio: {ex.Message}", ex);
            }
        }

        public void Stop()
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
                // PlaybackStopped event will be fired by _waveOut which calls OnPlaybackStopped
            }
            else
            {
                // If it was already null, ensure cleanup
                CleanupResources();
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            CleanupResources();
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        private void CleanupResources()
        {
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
