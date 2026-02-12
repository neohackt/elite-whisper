using System.Windows.Controls;
using System.Windows.Input;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views.Pages
{
    public partial class ConfigurationPage : UserControl
    {
        private ConfigurationViewModel? _viewModel;

        public ConfigurationPage()
        {
            InitializeComponent();
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                if (App.AppHost != null)
                {
                    _viewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ConfigurationViewModel>(App.AppHost.Services);
                    DataContext = _viewModel;
                    
                    // Set focus callback
                    _viewModel.SetFocusCallback(() => this.Focus());
                    
                    // Load saved hotkey
                    _viewModel.LoadSavedHotkey();
                }
            }

            // Add KeyDown handler to capture hotkey
            this.PreviewKeyDown += OnPreviewKeyDown;
            this.Focusable = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel?.IsCapturing == true)
            {
                e.Handled = true;
                _viewModel.CaptureKeyPress(e.Key, Keyboard.Modifiers);
            }
        }
    }
}
