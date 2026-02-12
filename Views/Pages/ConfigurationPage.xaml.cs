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
                    
                    // Load saved hotkey
                    _viewModel.LoadSavedHotkey();
                }
            }

            // Add KeyDown handler to capture hotkey
            this.KeyDown += OnKeyDown;
            this.Focusable = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel?.IsCapturing == true)
            {
                e.Handled = true;
                _viewModel.CaptureKeyPress(e.Key, Keyboard.Modifiers);
            }
        }
    }
}
