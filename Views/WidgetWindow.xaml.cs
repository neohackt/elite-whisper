using System;
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
            _viewModel.OnRecordButtonClicked += () => _ = HandleDictationToggle();
            _viewModel.OnExpandClicked += TransitionToExpanded;
            _viewModel.OnCollapseClicked += TransitionToCollapsed;
        }

        // ==================== HOVER HANDLERS (with Expanded guard) ====================
        
        private void WidgetWindow_MouseEnter(object sender, MouseEventArgs e)
        {
            // Critical: Do NOT hover-expand when in Expanded state
            if (_viewModel.VisualState == WidgetVisualState.Expanded) return;
            if (_viewModel.State == WidgetState.Hidden) return;
            
            _viewModel.VisualState = WidgetVisualState.HoverExpanded;
            AnimateToHoverExpanded();
        }

        private void WidgetWindow_MouseLeave(object sender, MouseEventArgs e)
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
            AnimateSize(320, 180, 328, 188);
            
            // Reassert Z-order on expand (prevents burial by fullscreen apps)
            Win32.SetWindowPos(_handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }

        private void TransitionToCollapsed()
        {
            _viewModel.VisualState = WidgetVisualState.Collapsed;
            AnimateSize(140, 48, 148, 52);
        }

        private void AnimateToHoverExpanded()
        {
            AnimateSize(240, 48, 248, 52);
        }

        private void AnimateToCollapsed()
        {
            AnimateSize(140, 48, 148, 52);
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
            
            // Update corner radius for expanded mode
            if (contentHeight > 48)
            {
                ContentLayer.CornerRadius = new CornerRadius(16);
                ShadowLayer.CornerRadius = new CornerRadius(18);
            }
            else
            {
                ContentLayer.CornerRadius = new CornerRadius(24);
                ShadowLayer.CornerRadius = new CornerRadius(26);
            }
        }

        // ==================== KEY HANDLERS ====================
        
        private void WidgetWindow_PreviewKeyDown(object sender, KeyEventArgs e)
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

        // ==================== HOTKEY HANDLER (UNCHANGED LOGIC) ====================
        
        private async void OnHotkeyPressed()
        {
            await HandleDictationToggle();
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
                    _dictationService.StartListening();
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
            this.Show();
            
            Win32.SetWindowPos(_handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, 
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
            
            // Ensure we start in collapsed state
            _viewModel.VisualState = WidgetVisualState.Collapsed;
            
            // Give focus so Escape key works
            this.Focusable = true;
            this.Focus();
        }

        private void HideOverlay()
        {
            this.Hide();
            _viewModel.State = WidgetState.Hidden;
            _viewModel.VisualState = WidgetVisualState.Collapsed;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            HideOverlay();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }
    }
}
