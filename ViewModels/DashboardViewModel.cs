using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using EliteWhisper.Services;

namespace EliteWhisper.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private AppPage _currentPage = AppPage.Home;

        [ObservableProperty]
        private string _appStatus = "Ready";

        [ObservableProperty]
        private string _statusColor = "#10B981"; // Green (Success) by default

        [ObservableProperty]
        private string _statusMessage = "Press F2 to dictate";

        [ObservableProperty]
        private string _selectedMicrophone = "Default Microphone";

        [ObservableProperty]
        private string _selectedModel = "Balanced";

        private readonly AIEngineService _aiEngine;

        public DashboardViewModel(AIEngineService aiEngine)
        {
            _aiEngine = aiEngine;
            _aiEngine.StateChanged += OnEngineStateChanged;
            
            // Initialize
            UpdateStatus(_aiEngine.State);
        }

        private void OnEngineStateChanged(object? sender, EngineState state)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateStatus(state));
        }

        private void UpdateStatus(EngineState state)
        {
            switch (state)
            {
                case EngineState.Idle:
                    AppStatus = "Idle";
                    StatusColor = "#6B7280"; // Gray
                    StatusMessage = "Engine not configured";
                    break;
                case EngineState.Loading:
                    AppStatus = "Loading...";
                    StatusColor = "#F59E0B"; // Amber
                    StatusMessage = "Loading AI model";
                    break;
                case EngineState.Ready:
                    AppStatus = "Ready";
                    StatusColor = "#10B981"; // Green
                    StatusMessage = "Press F2 to dictate";
                    break;
                case EngineState.Recording:
                    AppStatus = "Listening";
                    StatusColor = "#EF4444"; // Red
                    StatusMessage = "Recording audio...";
                    break;
                case EngineState.Processing:
                    AppStatus = "Thinking";
                    StatusColor = "#8B5CF6"; // Purple
                    StatusMessage = "Transcribing speech...";
                    break;
                case EngineState.Error:
                    AppStatus = "Error";
                    StatusColor = "#EF4444"; // Red
                    StatusMessage = "Check configuration";
                    break;
            }
        }

        [RelayCommand]
        private void NavigateTo(AppPage page)
        {
            CurrentPage = page;
        }

        // Helper properties for RadioButton binding
        public bool IsHomePage
        {
            get => CurrentPage == AppPage.Home;
            set { if (value) CurrentPage = AppPage.Home; }
        }

        public bool IsModelsPage
        {
            get => CurrentPage == AppPage.Models;
            set { if (value) CurrentPage = AppPage.Models; }
        }

        public bool IsConfigurationPage
        {
            get => CurrentPage == AppPage.Configuration;
            set { if (value) CurrentPage = AppPage.Configuration; }
        }

        public bool IsSoundPage
        {
            get => CurrentPage == AppPage.Sound;
            set { if (value) CurrentPage = AppPage.Sound; }
        }

        public bool IsHistoryPage
        {
            get => CurrentPage == AppPage.History;
            set { if (value) CurrentPage = AppPage.History; }
        }

        public bool IsAboutPage
        {
            get => CurrentPage == AppPage.About;
            set { if (value) CurrentPage = AppPage.About; }
        }

        partial void OnCurrentPageChanged(AppPage value)
        {
            OnPropertyChanged(nameof(IsHomePage));
            OnPropertyChanged(nameof(IsModelsPage));
            OnPropertyChanged(nameof(IsConfigurationPage));
            OnPropertyChanged(nameof(IsSoundPage));
            OnPropertyChanged(nameof(IsHistoryPage));
            OnPropertyChanged(nameof(IsAboutPage));
        }
    }
}
