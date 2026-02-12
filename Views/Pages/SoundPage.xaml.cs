using System.Windows.Controls;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views.Pages
{
    public partial class SoundPage : UserControl
    {
        private SoundViewModel? _viewModel;

        public SoundPage()
        {
            InitializeComponent();
            
            // We'll set DataContext from code-behind for simplicity, or ideally via dependency injection
            if (App.Current is App app)
            {
                _viewModel = app.Services.GetService(typeof(SoundViewModel)) as SoundViewModel;
                DataContext = _viewModel;
            }

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _viewModel?.StartMonitoring();
        }

        private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _viewModel?.StopMonitoring();
        }
    }
}
