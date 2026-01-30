using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;

namespace EliteWhisper.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private AppPage _currentPage = AppPage.Home;

        [ObservableProperty]
        private string _appStatus = "Ready";

        [ObservableProperty]
        private string _selectedMicrophone = "Default Microphone";

        [ObservableProperty]
        private string _selectedModel = "Balanced";

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
