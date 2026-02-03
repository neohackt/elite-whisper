using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using EliteWhisper.Services;

namespace EliteWhisper.ViewModels
{
    public partial class HistoryViewModel : ObservableObject
    {
        private readonly HistoryService _historyService;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _hasHistory = false;

        public ReadOnlyObservableCollection<DictationRecord> History => _historyService.History;
        
        public ICollectionView HistoryView { get; private set; }

        public HistoryViewModel(HistoryService historyService)
        {
            _historyService = historyService;

            // Setup CollectionView for filtering
            HistoryView = CollectionViewSource.GetDefaultView(History);
            HistoryView.Filter = FilterHistory;
            
            // Monitor count changes
            ((INotifyPropertyChanged)History).PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == "Count") UpdateHasHistory();
            };
            UpdateHasHistory();
            
            // Initial sort if needed (Service already sorts, but view can too)
            HistoryView.SortDescriptions.Add(new SortDescription("Timestamp", ListSortDirection.Descending));
        }

        private void UpdateHasHistory()
        {
            HasHistory = History.Count > 0;
        }

        partial void OnSearchTextChanged(string value)
        {
            HistoryView.Refresh();
        }

        private bool FilterHistory(object item)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (item is DictationRecord record)
            {
                return record.Content.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        [RelayCommand]
        private void CopyText(DictationRecord? record)
        {
            if (record != null && !string.IsNullOrEmpty(record.Content))
            {
                Clipboard.SetText(record.Content);
            }
        }

        [RelayCommand]
        private void DeleteRecord(DictationRecord? record)
        {
            if (record != null)
            {
                _historyService.DeleteRecord(record.Id);
            }
        }

        [RelayCommand]
        private void ClearAll()
        {
            if (MessageBox.Show("Are you sure you want to clear all history?", "Clear History", 
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _historyService.ClearAll();
            }
        }
    }
}
