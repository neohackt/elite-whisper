using System;
using System.IO;
using System.Windows.Forms; // For FolderBrowserDialog
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Services;

namespace EliteWhisper.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly WhisperConfigurationService _configService;
        private readonly HistoryService _historyService;

        [ObservableProperty]
        private string _historyPath;

        public MainViewModel(WhisperConfigurationService configService, HistoryService historyService)
        {
            _configService = configService;
            _historyService = historyService;

            // Load initial path
            _historyPath = _configService.CurrentConfiguration.HistoryStoragePath 
                           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EliteWhisper");
        }

        [RelayCommand]
        private void BrowseHistoryFolder()
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select a folder to store dictation history";
            dialog.UseDescriptionForTitle = true;
            dialog.SelectedPath = HistoryPath;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string path = dialog.SelectedPath;
                    _configService.SetHistoryStoragePath(path);
                    _historyService.RefreshLocation();
                    HistoryPath = path;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error setting history path: {ex.Message}", "Error");
                }
            }
        }
    }
}
