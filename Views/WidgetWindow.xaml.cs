using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using EliteWhisper.Models;
using EliteWhisper.Native;
using EliteWhisper.Services;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views
{
    public partial class WidgetWindow : Window
    {
        private readonly WidgetViewModel _viewModel;
        private readonly HotkeyService _hotkeyService;
        private readonly DictationService _dictationService;
        private IntPtr _handle;
        private bool _isShuttingDown;
        private bool _isDisposed;
        private System.Windows.Point _lastKnownPosition;

        // P/Invoke for screen positioning without Windows Forms
        private struct POINT { public int X; public int Y; }
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        private const int MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        public WidgetWindow(
            WidgetViewModel viewModel, 
            HotkeyService hotkeyService,
            DictationService dictationService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _hotkeyService = hotkeyService;
            _dictationService = dictationService;
            DataContext = _viewModel;

            // Win32 hooks
            SourceInitialized += WidgetWindow_SourceInitialized;
            Loaded += WidgetWindow_Loaded;
            PreviewKeyDown += WidgetWindow_PreviewKeyDown;
            
            // Hover events for visual state transitions
            MouseEnter += WidgetWindow_MouseEnter;
            MouseLeave += WidgetWindow_MouseLeave;
            
            // Subscribe to hotkey
            _hotkeyService.OnHotkeyPressed += OnHotkeyPressed;
            
            // Subscribe to ViewModel events (button clicks mirror hotkey)
            _viewModel.OnRecordButtonClicked += OnRecordButtonClicked;
            _viewModel.OnExpandClicked += TransitionToExpanded;
            _viewModel.OnCollapseClicked += TransitionToCollapsed;
        }

        // ==================== HOVER HANDLERS (with Expanded guard) ====================
        
        private void WidgetWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Critical: Do NOT hover-expand when in Expanded state
            if (_viewModel.VisualState == WidgetVisualState.Expanded) return;
            if (_viewModel.State == WidgetState.Hidden) return;
            
            _viewModel.VisualState = WidgetVisualState.HoverExpanded;
            AnimateToHoverExpanded();
        }

        private void WidgetWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Critical: Do NOT collapse when in Expanded state
            if (_viewModel.VisualState == WidgetVisualState.Expanded) return;
            if (_viewModel.State == WidgetState.Hidden) return;
            
            _viewModel.VisualState = WidgetVisualState.Collapsed;
            AnimateToCollapsed();
        }

        // ==================== VISUAL TRANSITIONS ====================
        
        private void TransitionToExpanded()
        {
            _viewModel.VisualState = WidgetVisualState.Expanded;
            AnimateSize(288, 52, 296, 56);
            
            // Reassert Z-order on expand (prevents burial by fullscreen apps)
            Win32.SetWindowPos(_handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        private void TransitionToCollapsed()
        {
            _viewModel.VisualState = WidgetVisualState.Collapsed;
            AnimateSize(96, 36, 104, 40);
        }

        private void AnimateToHoverExpanded()
        {
            AnimateSize(200, 40, 208, 44);
        }

        private void AnimateToCollapsed()
        {
            AnimateSize(96, 36, 104, 40);
        }

        /// <summary>
        /// Animate both content layer and shadow layer sizes.
        /// Shadow is always slightly larger than content.
        /// </summary>
        private void AnimateSize(double contentWidth, double contentHeight, double shadowWidth, double shadowHeight)
        {
            var duration = TimeSpan.FromMilliseconds(160);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            // Content layer animations
            var contentWidthAnim = new DoubleAnimation(contentWidth, duration) { EasingFunction = ease };
            var contentHeightAnim = new DoubleAnimation(contentHeight, duration) { EasingFunction = ease };
            ContentLayer.BeginAnimation(MinWidthProperty, contentWidthAnim);
            ContentLayer.BeginAnimation(HeightProperty, contentHeightAnim);

            // Shadow layer animations (slightly larger, offset down)
            var shadowWidthAnim = new DoubleAnimation(shadowWidth, duration) { EasingFunction = ease };
            var shadowHeightAnim = new DoubleAnimation(shadowHeight, duration) { EasingFunction = ease };
            ShadowLayer.BeginAnimation(MinWidthProperty, shadowWidthAnim);
            ShadowLayer.BeginAnimation(HeightProperty, shadowHeightAnim);
            
            // Update corner radius for expanded mode (capsule = height/2)
            double contentCornerRadius = contentHeight / 2.0;
            double shadowCornerRadius = shadowHeight / 2.0;
            
            ContentLayer.CornerRadius = new CornerRadius(contentCornerRadius);
            ShadowLayer.CornerRadius = new CornerRadius(shadowCornerRadius);
        }

        // ==================== KEY HANDLERS ====================
        
        private void WidgetWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_viewModel.VisualState == WidgetVisualState.Expanded)
                {
                    TransitionToCollapsed();
                }
                else
                {
                    HideOverlay();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.S && _viewModel.State == WidgetState.Ready)
            {
                var mainWindow = App.AppHost?.Services.GetService(typeof(MainWindow)) as MainWindow;
                mainWindow?.Show();
                mainWindow?.Activate();
                e.Handled = true;
            }
        }

        // ==================== WIN32 SETUP ====================
        
        private void WidgetWindow_SourceInitialized(object? sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;

            int exStyle = Win32.GetWindowLong(_handle, Win32.GWL_EXSTYLE);
            exStyle |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
            Win32.SetWindowLong(_handle, Win32.GWL_EXSTYLE, exStyle);
            
            // Register hotkey here - handle is now valid
            _hotkeyService.Register(_handle);
        }

        private void WidgetWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Start hidden
            HideOverlay();
        }

        // ==================== HOTKEY HANDLER ====================
        
        private void OnHotkeyPressed()
        {
            if (_isShuttingDown || _isDisposed) return;
            _ = HandleDictationToggle();
        }

        /// <summary>
        /// Shared dictation toggle logic - used by BOTH hotkey and record button.
        /// </summary>
        private async System.Threading.Tasks.Task HandleDictationToggle()
        {
            switch (_viewModel.State)
            {
                case WidgetState.Hidden:
                    ShowOverlay();
                    _viewModel.State = WidgetState.Ready;
                    break;

                case WidgetState.Ready:
                    _dictationService.StartListening(RecordingSource.Widget);
                    break;

                case WidgetState.Listening:
                    await _dictationService.StopListeningAndProcessAsync();
                    break;

                case WidgetState.Processing:
                    // No-op during processing
                    break;
            }
        }

        // ==================== OVERLAY VISIBILITY ====================
        
        private void ShowOverlay()
        {
            if (_isShuttingDown || _isDisposed) return;
            
            // Position at bottom-center of active screen before showing
            PositionAtBottomCenter();
            
            this.Show();
            
            Win32.SetWindowPos(_handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, 
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
            
            // Ensure we start in collapsed state
            if (_viewModel != null)
            {
                _viewModel.VisualState = WidgetVisualState.Collapsed;
            }
            
            // Give focus so Escape key works
            this.Focusable = true;
            this.Focus();
        }

        /// <summary>
        /// Positions the widget at the bottom-center of the active screen,
        /// respecting working area bounds, taskbars, and DPI scaling.
        /// Uses P/Invoke instead of Windows Forms.
        /// </summary>
        private void PositionAtBottomCenter()
        {
            try
            {
                // Get cursor position
                if (!GetCursorPos(out POINT cursorPos))
                    return;

                // Get monitor nearest to cursor
                IntPtr hMonitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
                if (hMonitor == IntPtr.Zero)
                    return;

                // Get monitor info (includes working area excluding taskbar)
                MONITORINFO mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(hMonitor, ref mi))
                    return;

                // Get DPI for this monitor
                uint dpiX, dpiY;
                GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                double dpiScaleX = dpiX / 96.0;
                double dpiScaleY = dpiY / 96.0;

                // Widget dimensions (collapsed state)
                double widgetWidth = 96;
                double widgetHeight = 36;

                // Working area (excludes taskbar)
                int workLeft = mi.rcWork.Left;
                int workTop = mi.rcWork.Top;
                int workRight = mi.rcWork.Right;
                int workBottom = mi.rcWork.Bottom;
                double workWidth = workRight - workLeft;
                double workHeight = workBottom - workTop;

                // Widget dimensions in physical pixels
                double widgetWidthPx = widgetWidth * (dpiX / 96.0);
                double widgetHeightPx = widgetHeight * (dpiY / 96.0);

                // Center horizontally, 24px margin from bottom
                double leftPx = workLeft + (workWidth - widgetWidthPx) / 2.0;
                double topPx = workBottom - widgetHeightPx - 24 * (dpiY / 96.0);

                // Convert to WPF device-independent units
                double left = leftPx / (dpiX / 96.0);
                double top = topPx / (dpiY / 96.0);

                this.Left = left;
                this.Top = top;
                
                _lastKnownPosition = new System.Windows.Point(left, top);
            }
            catch
            {
                // Fallback: center of primary screen
                this.Left = 100;
                this.Top = 100;
            }
        }

        private void HideOverlay()
        {
            if (_isDisposed) return;
            
            // Save position before hiding
            _lastKnownPosition = new System.Windows.Point(this.Left, this.Top);
            
            try
            {
                this.Hide();
            }
            catch
            {
                // Ignore hide errors during shutdown
            }

            // Null-safe: viewmodel may be disposed during shutdown
            if (_viewModel != null)
            {
                try
                {
                    _viewModel.State = WidgetState.Hidden;
                    _viewModel.VisualState = WidgetVisualState.Collapsed;
                }
                catch
                {
                    // Ignore VM errors during shutdown
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // During application shutdown, allow the window to close
            // Check if the application is shutting down
            if (System.Windows.Application.Current == null || _isShuttingDown)
            {
                _isShuttingDown = true;
                // Allow close during actual shutdown
                e.Cancel = false;
                return;
            }

            // Normal operation: hide to tray instead of closing
            e.Cancel = true;
            HideOverlay();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
            
            // Update last known position after drag
            _lastKnownPosition = new System.Windows.Point(this.Left, this.Top);
        }

        /// <summary>
        /// Performs deterministic cleanup of widget resources.
        /// Called during actual application shutdown.
        /// </summary>
        internal void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isShuttingDown = true;

            // Unsubscribe from hotkey service
            try
            {
                _hotkeyService.OnHotkeyPressed -= OnHotkeyPressed;
            }
            catch { }

            // Unsubscribe from ViewModel events
            try
            {
                _viewModel.OnRecordButtonClicked -= OnRecordButtonClicked;
                _viewModel.OnExpandClicked -= TransitionToExpanded;
                _viewModel.OnCollapseClicked -= TransitionToCollapsed;
            }
            catch { }

            // Unsubscribe from window events
            try
            {
                SourceInitialized -= WidgetWindow_SourceInitialized;
                Loaded -= WidgetWindow_Loaded;
                PreviewKeyDown -= WidgetWindow_PreviewKeyDown;
                MouseEnter -= WidgetWindow_MouseEnter;
                MouseLeave -= WidgetWindow_MouseLeave;
            }
            catch { }

            // Unregister hotkey
            try
            {
                _hotkeyService.Unregister(_handle);
            }
            catch { }
        }

        /// <summary>
        /// Handles record button click from ViewModel.
        /// </summary>
        private void OnRecordButtonClicked()
        {
            if (_isShuttingDown || _isDisposed) return;
            _ = HandleDictationToggle();
        }
    }
}