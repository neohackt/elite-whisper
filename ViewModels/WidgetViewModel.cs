using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using System;
using System.Windows.Media;

namespace EliteWhisper.ViewModels
{
    public partial class WidgetViewModel : ObservableObject
    {
        // ==================== DICTATION STATE ====================
        
        private WidgetState _state = WidgetState.Hidden;
        
        public WidgetState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    UpdateVisuals();
                    OnPropertyChanged(nameof(IsListening));
                    OnPropertyChanged(nameof(IsProcessing));
                    OnPropertyChanged(nameof(RecordButtonTooltip));
                }
            }
        }

        // ==================== VISUAL STATE ====================
        
        private WidgetVisualState _visualState = WidgetVisualState.Collapsed;
        
        public WidgetVisualState VisualState
        {
            get => _visualState;
            set
            {
                if (SetProperty(ref _visualState, value))
                {
                    OnPropertyChanged(nameof(IsCollapsed));
                    OnPropertyChanged(nameof(IsHoverExpanded));
                    OnPropertyChanged(nameof(IsExpanded));
                    OnPropertyChanged(nameof(ContainerWidth));
                    OnPropertyChanged(nameof(ContainerHeight));
                }
            }
        }

        // ==================== VISUAL STATE HELPERS ====================
        
        public bool IsCollapsed => VisualState == WidgetVisualState.Collapsed;
        public bool IsHoverExpanded => VisualState == WidgetVisualState.HoverExpanded;
        public bool IsExpanded => VisualState == WidgetVisualState.Expanded;
        
        // Container dimensions for animation
        public double ContainerWidth => VisualState switch
        {
            WidgetVisualState.Collapsed => 140,
            WidgetVisualState.HoverExpanded => 240,
            WidgetVisualState.Expanded => 320,
            _ => 140
        };
        
        public double ContainerHeight => VisualState switch
        {
            WidgetVisualState.Collapsed => 48,
            WidgetVisualState.HoverExpanded => 48,
            WidgetVisualState.Expanded => 180,
            _ => 48
        };

        // ==================== MIC LEVEL ====================
        
        private float _micLevel;
        
        public float MicLevel
        {
            get => _micLevel;
            set => SetProperty(ref _micLevel, value);
        }

        // ==================== STATUS DISPLAY ====================
        
        private string _statusText = "";
        private SolidColorBrush _stateColor = new SolidColorBrush(Colors.Gray);

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public SolidColorBrush StateColor
        {
            get => _stateColor;
            set => SetProperty(ref _stateColor, value);
        }

        // ==================== DICTATION STATE HELPERS ====================
        
        public bool IsListening => State == WidgetState.Listening;
        public bool IsProcessing => State == WidgetState.Processing;
        
        public string RecordButtonTooltip => State switch
        {
            WidgetState.Listening => "Stop recording (F2)",
            _ => "Start recording (F2)"
        };

        // ==================== EVENTS ====================
        
        /// <summary>Raised when user clicks record button (to mirror hotkey)</summary>
        public event Action? OnRecordButtonClicked;
        
        /// <summary>Raised when user clicks expand button</summary>
        public event Action? OnExpandClicked;
        
        /// <summary>Raised when user clicks collapse/close button</summary>
        public event Action? OnCollapseClicked;

        // ==================== COMMANDS ====================
        
        [RelayCommand]
        private void ToggleRecording()
        {
            OnRecordButtonClicked?.Invoke();
        }
        
        [RelayCommand]
        private void Expand()
        {
            OnExpandClicked?.Invoke();
        }
        
        [RelayCommand]
        private void Collapse()
        {
            OnCollapseClicked?.Invoke();
        }

        // ==================== CONSTRUCTOR ====================
        
        public WidgetViewModel()
        {
            State = WidgetState.Hidden;
            VisualState = WidgetVisualState.Collapsed;
            InitializeWaveform();
        }

        // ==================== WAVEFORM VISUALIZATION ====================
        
        // Number of bars in expanded waveform
        public const int ExpandedBarCount = 20;
        
        // Number of bars in mini waveform (collapsed pill)
        public const int MiniBarCount = 5;
        
        // EMA smoothing factor (0.0 = no change, 1.0 = instant)
        private const double SmoothingFactor = 0.3;
        
        // Amplitude arrays for waveform visualization
        private double[] _expandedAmplitudes = new double[ExpandedBarCount];
        private double[] _miniAmplitudes = new double[MiniBarCount];
        
        // SPATIAL FALLOFF: Precomputed bell curves (center = 1.0, edges = lower)
        private readonly double[] _expandedSpatialWeights = new double[ExpandedBarCount];
        private readonly double[] _miniSpatialWeights = new double[MiniBarCount];
        
        // SLOW-CHANGING NOISE: Per-bar variance that changes slowly over time
        private readonly double[] _expandedNoisePhase = new double[ExpandedBarCount];
        private readonly double[] _miniNoisePhase = new double[MiniBarCount];
        
        // Random for noise
        private readonly Random _random = new();
        
        // Frame counter for slow noise evolution
        private int _noiseFrame = 0;
        
        /// <summary>
        /// Amplitude values for expanded widget waveform (20 bars).
        /// Values range from 0.0 to 1.0.
        /// </summary>
        public double[] ExpandedAmplitudes
        {
            get => _expandedAmplitudes;
            private set => SetProperty(ref _expandedAmplitudes, value);
        }
        
        /// <summary>
        /// Amplitude values for mini waveform in collapsed pill (5 bars).
        /// Values range from 0.0 to 1.0.
        /// </summary>
        public double[] MiniAmplitudes
        {
            get => _miniAmplitudes;
            private set => SetProperty(ref _miniAmplitudes, value);
        }
        
        private void InitializeWaveform()
        {
            _expandedAmplitudes = new double[ExpandedBarCount];
            _miniAmplitudes = new double[MiniBarCount];
            
            // Precompute spatial falloff weights (bell curve using cosine)
            // Center bars = 1.0, edge bars = ~0.3
            double expandedCenter = (ExpandedBarCount - 1) / 2.0;
            for (int i = 0; i < ExpandedBarCount; i++)
            {
                double normalized = (i - expandedCenter) / expandedCenter; // -1 to 1
                // Cosine falloff: cos(x * π/2) gives bell curve shape
                // Lower floor (0.1) so edges nearly vanish, center dominates
                _expandedSpatialWeights[i] = 0.1 + 0.9 * Math.Cos(normalized * Math.PI / 2);
                // Initialize noise phase randomly
                _expandedNoisePhase[i] = _random.NextDouble() * Math.PI * 2;
            }
            
            double miniCenter = (MiniBarCount - 1) / 2.0;
            for (int i = 0; i < MiniBarCount; i++)
            {
                double normalized = (i - miniCenter) / Math.Max(miniCenter, 0.5);
                _miniSpatialWeights[i] = 0.15 + 0.85 * Math.Cos(normalized * Math.PI / 2);
                _miniNoisePhase[i] = _random.NextDouble() * Math.PI * 2;
            }
        }

        // ==================== STATE VISUALS ====================
        
        private void UpdateVisuals()
        {
            switch (State)
            {
                case WidgetState.Hidden:
                    StatusText = "";
                    StateColor = new SolidColorBrush(Colors.Transparent);
                    MicLevel = 0;
                    ResetWaveform();
                    break;
                case WidgetState.Ready:
                    StatusText = "Ready";
                    StateColor = new SolidColorBrush(Color.FromRgb(107, 114, 128)); // Gray-500
                    MicLevel = 0;
                    ResetWaveform();
                    break;
                case WidgetState.Listening:
                    StatusText = "Listening...";
                    StateColor = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red-500
                    break;
                case WidgetState.Processing:
                    StatusText = "Processing...";
                    StateColor = new SolidColorBrush(Color.FromRgb(99, 102, 241)); // Indigo-500
                    MicLevel = 0;
                    ResetWaveform();
                    break;
            }
        }
        
        private void ResetWaveform()
        {
            for (int i = 0; i < ExpandedBarCount; i++)
                _expandedAmplitudes[i] = 0;
            for (int i = 0; i < MiniBarCount; i++)
                _miniAmplitudes[i] = 0;
            OnPropertyChanged(nameof(ExpandedAmplitudes));
            OnPropertyChanged(nameof(MiniAmplitudes));
        }

        /// <summary>
        /// Update mic level and waveform amplitudes with EMA smoothing.
        /// Called from DictationService with normalized level (0-1).
        /// </summary>
        public void UpdateMicLevel(float level)
        {
            if (State == WidgetState.Listening)
            {
                MicLevel = level * 100;
                UpdateWaveformAmplitudes(level);
            }
        }
        
        /// <summary>
        /// Calculate speech-like waveform amplitudes.
        /// 
        /// Formula: barHeight[i] = micLevel × spatialFalloff[i] × noiseVariation[i]
        /// 
        /// - spatialFalloff: Bell curve (center = 1.0, edges = 0.3)
        /// - noiseVariation: Slow-changing sine wave per bar (organic feel)
        /// - EMA smoothing: Smooth transitions
        /// </summary>
        private void UpdateWaveformAmplitudes(float baseLevel)
        {
            _noiseFrame++;
            
            // Clamp input to prevent spikes
            double clampedLevel = Math.Clamp(baseLevel, 0.0, 1.0);
            
            // Boost low levels for visibility (speech often quiet)
            double boostedLevel = Math.Pow(clampedLevel, 0.5);
            
            // Create new arrays to force WPF binding update
            var newExpanded = new double[ExpandedBarCount];
            var newMini = new double[MiniBarCount];
            
            // Update expanded waveform bars
            for (int i = 0; i < ExpandedBarCount; i++)
            {
                // Slow-changing noise using sine wave (subtle ±15%)
                // Each bar has unique phase offset for decorrelation
                double noise = 0.85 + 0.15 * Math.Sin(_expandedNoisePhase[i] + _noiseFrame * 0.04);
                
                // Target = micLevel × spatial weight × noise
                double target = boostedLevel * _expandedSpatialWeights[i] * noise;
                
                // EMA smoothing
                newExpanded[i] = _expandedAmplitudes[i] + SmoothingFactor * (target - _expandedAmplitudes[i]);
                // Clamp to [0.05, 1.0] - prevents flat line at silence
                newExpanded[i] = Math.Clamp(newExpanded[i], 0.0, 1.0);
            }
            
            // Update mini waveform bars (calmer motion)
            for (int i = 0; i < MiniBarCount; i++)
            {
                // Subtle noise (±12%) with slower phase advance
                double noise = 0.88 + 0.12 * Math.Sin(_miniNoisePhase[i] + _noiseFrame * 0.03);
                double target = boostedLevel * _miniSpatialWeights[i] * noise;
                
                // Slower smoothing for mini wave
                newMini[i] = _miniAmplitudes[i] + (SmoothingFactor * 0.5) * (target - _miniAmplitudes[i]);
                newMini[i] = Math.Clamp(newMini[i], 0.0, 1.0);
            }
            
            // Assign new arrays (triggers PropertyChanged AND dependency property callback)
            _expandedAmplitudes = newExpanded;
            _miniAmplitudes = newMini;
            
            OnPropertyChanged(nameof(ExpandedAmplitudes));
            OnPropertyChanged(nameof(MiniAmplitudes));
        }
    }
}

