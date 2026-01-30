using System;
using System.Windows;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views
{
    public partial class MainWindow : Window
    {
        private readonly DashboardViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new DashboardViewModel();
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
