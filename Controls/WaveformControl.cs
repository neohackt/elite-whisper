using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EliteWhisper.Controls
{
    /// <summary>
    /// A speech waveform visualization using DUAL-BAR structure.
    /// 
    /// Each bar position has TWO thin vertical strokes:
    /// - Top stroke (extends upward from center)
    /// - Bottom stroke (extends downward from center)
    /// - Visible gap in the middle
    /// 
    /// This matches the reference speech waveform visual.
    /// NO ScaleTransform - direct Height manipulation.
    /// </summary>
    public class WaveformControl : Canvas
    {
        // ==================== DEPENDENCY PROPERTIES ====================
        
        public static readonly DependencyProperty AmplitudesProperty =
            DependencyProperty.Register(
                nameof(Amplitudes),
                typeof(double[]),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(null, OnAmplitudesChanged));

        public static readonly DependencyProperty BarCountProperty =
            DependencyProperty.Register(
                nameof(BarCount),
                typeof(int),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(20, OnBarCountChanged));

        public static readonly DependencyProperty BarWidthProperty =
            DependencyProperty.Register(
                nameof(BarWidth),
                typeof(double),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(2.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty BarSpacingProperty =
            DependencyProperty.Register(
                nameof(BarSpacing),
                typeof(double),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(3.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty MaxBarHeightProperty =
            DependencyProperty.Register(
                nameof(MaxBarHeight),
                typeof(double),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(20.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty MinBarHeightProperty =
            DependencyProperty.Register(
                nameof(MinBarHeight),
                typeof(double),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(2.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty CenterGapProperty =
            DependencyProperty.Register(
                nameof(CenterGap),
                typeof(double),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(2.0, OnLayoutPropertyChanged));

        public static readonly DependencyProperty BarFillProperty =
            DependencyProperty.Register(
                nameof(BarFill),
                typeof(Brush),
                typeof(WaveformControl),
                new FrameworkPropertyMetadata(
                    new SolidColorBrush(Color.FromRgb(78, 161, 255)), // #4EA1FF
                    OnBarFillChanged));

        // ==================== PROPERTIES ====================
        
        public double[]? Amplitudes
        {
            get => (double[]?)GetValue(AmplitudesProperty);
            set => SetValue(AmplitudesProperty, value);
        }

        public int BarCount
        {
            get => (int)GetValue(BarCountProperty);
            set => SetValue(BarCountProperty, value);
        }

        public double BarWidth
        {
            get => (double)GetValue(BarWidthProperty);
            set => SetValue(BarWidthProperty, value);
        }

        public double BarSpacing
        {
            get => (double)GetValue(BarSpacingProperty);
            set => SetValue(BarSpacingProperty, value);
        }

        /// <summary>
        /// Maximum height of each stroke (top or bottom).
        /// Total bar height at max amplitude = MaxBarHeight * 2 + CenterGap
        /// </summary>
        public double MaxBarHeight
        {
            get => (double)GetValue(MaxBarHeightProperty);
            set => SetValue(MaxBarHeightProperty, value);
        }

        /// <summary>
        /// Minimum height of each stroke at zero amplitude.
        /// </summary>
        public double MinBarHeight
        {
            get => (double)GetValue(MinBarHeightProperty);
            set => SetValue(MinBarHeightProperty, value);
        }

        /// <summary>
        /// Gap between top and bottom strokes (center line gap).
        /// </summary>
        public double CenterGap
        {
            get => (double)GetValue(CenterGapProperty);
            set => SetValue(CenterGapProperty, value);
        }

        public Brush BarFill
        {
            get => (Brush)GetValue(BarFillProperty);
            set => SetValue(BarFillProperty, value);
        }

        // ==================== PRIVATE FIELDS ====================
        
        // Each bar position has TWO rectangles: top stroke and bottom stroke
        private Rectangle[] _topStrokes = Array.Empty<Rectangle>();
        private Rectangle[] _bottomStrokes = Array.Empty<Rectangle>();
        private bool _barsCreated = false;

        // ==================== CONSTRUCTOR ====================
        
        public WaveformControl()
        {
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_barsCreated)
                {
                    CreateBars();
                    UpdateBars();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                if (!_barsCreated)
                {
                    CreateBars();
                }
                else
                {
                    UpdateBarPositions();
                }
                UpdateBars();
            }
        }

        // ==================== PROPERTY CHANGE HANDLERS ====================
        
        private static void OnAmplitudesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WaveformControl control)
            {
                control.UpdateBars();
            }
        }

        private static void OnBarCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WaveformControl control)
            {
                control._barsCreated = false;
                control.CreateBars();
                control.UpdateBars();
            }
        }

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WaveformControl control)
            {
                control._barsCreated = false;
                control.CreateBars();
                control.UpdateBars();
            }
        }

        private static void OnBarFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WaveformControl control && e.NewValue is Brush brush)
            {
                foreach (var stroke in control._topStrokes)
                    stroke.Fill = brush;
                foreach (var stroke in control._bottomStrokes)
                    stroke.Fill = brush;
            }
        }

        // ==================== BAR MANAGEMENT ====================
        
        private void CreateBars()
        {
            double canvasWidth = ActualWidth > 0 ? ActualWidth : Width;
            double canvasHeight = ActualHeight > 0 ? ActualHeight : Height;
            
            if (double.IsNaN(canvasWidth) || double.IsNaN(canvasHeight))
                return;
            if (canvasWidth <= 0 || canvasHeight <= 0 || BarCount <= 0)
                return;

            // Clear existing elements
            Children.Clear();
            _topStrokes = new Rectangle[BarCount];
            _bottomStrokes = new Rectangle[BarCount];

            // Calculate horizontal layout
            double totalWidth = BarCount * BarWidth + (BarCount - 1) * BarSpacing;
            double startX = (canvasWidth - totalWidth) / 2;
            double centerY = canvasHeight / 2;

            for (int i = 0; i < BarCount; i++)
            {
                double x = startX + i * (BarWidth + BarSpacing);

                // Top stroke (extends upward from center)
                var topStroke = new Rectangle
                {
                    Width = BarWidth,
                    Height = MinBarHeight,
                    Fill = BarFill,
                    RadiusX = BarWidth / 2,
                    RadiusY = BarWidth / 2,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true
                };
                Canvas.SetLeft(topStroke, x);
                // Top stroke positioned above center gap (grows upward)
                Canvas.SetTop(topStroke, centerY - CenterGap / 2 - MinBarHeight);
                _topStrokes[i] = topStroke;
                Children.Add(topStroke);

                // Bottom stroke (extends downward from center)
                var bottomStroke = new Rectangle
                {
                    Width = BarWidth,
                    Height = MinBarHeight,
                    Fill = BarFill,
                    RadiusX = BarWidth / 2,
                    RadiusY = BarWidth / 2,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true
                };
                Canvas.SetLeft(bottomStroke, x);
                // Bottom stroke positioned below center gap
                Canvas.SetTop(bottomStroke, centerY + CenterGap / 2);
                _bottomStrokes[i] = bottomStroke;
                Children.Add(bottomStroke);
            }
            
            _barsCreated = true;
        }

        private void UpdateBars()
        {
            if (_topStrokes.Length == 0 || _bottomStrokes.Length == 0) return;

            double canvasHeight = ActualHeight > 0 ? ActualHeight : Height;
            if (canvasHeight <= 0) return;
            
            double centerY = canvasHeight / 2;
            var amplitudes = Amplitudes ?? Array.Empty<double>();

            for (int i = 0; i < _topStrokes.Length && i < _bottomStrokes.Length; i++)
            {
                // Get amplitude for this bar (use 0 if array is smaller)
                double amplitude = i < amplitudes.Length ? amplitudes[i] : 0.0;
                amplitude = Math.Clamp(amplitude, 0.0, 1.0);

                // Calculate stroke height: MinBarHeight at 0, MaxBarHeight at 1
                double strokeHeight = MinBarHeight + (MaxBarHeight - MinBarHeight) * amplitude;
                
                // Update top stroke (grows upward from center gap)
                _topStrokes[i].Height = strokeHeight;
                Canvas.SetTop(_topStrokes[i], centerY - CenterGap / 2 - strokeHeight);
                
                // Update bottom stroke (grows downward from center gap)
                _bottomStrokes[i].Height = strokeHeight;
                // Bottom stroke top position stays at center + gap/2
                Canvas.SetTop(_bottomStrokes[i], centerY + CenterGap / 2);
            }
        }

        private void UpdateBarPositions()
        {
            if (_topStrokes.Length == 0) return;

            double canvasWidth = ActualWidth > 0 ? ActualWidth : Width;
            double canvasHeight = ActualHeight > 0 ? ActualHeight : Height;
            
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            double totalWidth = BarCount * BarWidth + (BarCount - 1) * BarSpacing;
            double startX = (canvasWidth - totalWidth) / 2;

            for (int i = 0; i < _topStrokes.Length && i < _bottomStrokes.Length; i++)
            {
                double x = startX + i * (BarWidth + BarSpacing);
                Canvas.SetLeft(_topStrokes[i], x);
                Canvas.SetLeft(_bottomStrokes[i], x);
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateBarPositions();
            UpdateBars();
        }
    }
}
