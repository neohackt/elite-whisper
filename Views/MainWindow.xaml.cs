using System;
using System.Windows;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views
{
    public partial class MainWindow : Window
    {
        private readonly DashboardViewModel _viewModel;

        public MainWindow(DashboardViewModel viewModel)
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.ToString(),
                    "XAML Load Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                throw;
            }
            
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Hide instead of close (stay in tray)
            e.Cancel = true;
            this.Hide();
        }
    }
}
