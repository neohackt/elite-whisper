using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace EliteWhisper.Controls
{
    public class AnimatedCounter : INotifyPropertyChanged
    {
        private int _value;
        public int Value
        {
            get => _value;
            private set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public async Task AnimateTo(int target, int durationMs = 600)
        {
            int start = Value;
            if (start == target) return;

            int steps = durationMs / 16; // ~60 FPS
            if (steps < 1) steps = 1;

            double stepValue = (double)(target - start) / steps;
            
            // Simple ease-out
            // We can do a basic loop
            
            var startTime = DateTime.Now;
            
            while (true)
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                if (elapsed >= durationMs)
                {
                    Value = target;
                    break;
                }

                double progress = elapsed / durationMs;
                // Ease out cubic
                progress = 1 - Math.Pow(1 - progress, 3);
                
                int current = (int)(start + (target - start) * progress);
                Value = current;
                
                await Task.Delay(16);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
