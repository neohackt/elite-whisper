using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    public class HistoryService
    {
        private readonly WhisperConfigurationService _configService;
        private ObservableCollection<DictationRecord> _history = new();
        private const string HISTORY_FILENAME = "history.json";

        public ReadOnlyObservableCollection<DictationRecord> History => new(_history);

        public HistoryService(WhisperConfigurationService configService)
        {
            _configService = configService;
            LoadHistory();
        }

        public void AddRecord(DictationRecord record)
        {
            // Add to in-memory list (start)
            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                _history.Insert(0, record);
            });
            
            // Persist async
            Task.Run(SaveHistory);
        }

        public void DeleteRecord(Guid id)
        {
            var record = _history.FirstOrDefault(r => r.Id == id);
            if (record != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _history.Remove(record);
                });
                Task.Run(SaveHistory);
            }
        }
        
        public void ClearAll()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _history.Clear();
            });
            Task.Run(SaveHistory);
        }

        private void LoadHistory()
        {
            try
            {
                string path = GetHistoryFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var records = JsonSerializer.Deserialize<List<DictationRecord>>(json);
                    
                    if (records != null)
                    {
                        // Load into observable collection on UI thread if needed (but ctor is usually early enough)
                        // Safety: Assuming Ctor is called on UI thread or before binding
                        _history = new ObservableCollection<DictationRecord>(
                            records.OrderByDescending(x => x.Timestamp));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load history: {ex.Message}");
            }
        }

        private void SaveHistory()
        {
            try
            {
                string path = GetHistoryFilePath();
                
                // Ensure directory exists
                string? dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_history.ToList(), options);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save history: {ex.Message}");
            }
        }

        private string GetHistoryFilePath()
        {
            // Use custom path if set, otherwise fallback to AppData
            string? customPath = _configService.CurrentConfiguration.HistoryStoragePath;
            
            if (!string.IsNullOrEmpty(customPath))
            {
                return Path.Combine(customPath, HISTORY_FILENAME);
            }
            
            // Fallback to local app data
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EliteWhisper");
                
            return Path.Combine(appData, HISTORY_FILENAME);
        }
        
        /// <summary>
        /// Reloads history if the storage path changes
        /// </summary>
        public void RefreshLocation()
        {
             // Clear current and reload from new location
             System.Windows.Application.Current.Dispatcher.Invoke(() => _history.Clear());
             LoadHistory();
        }
    }
}
