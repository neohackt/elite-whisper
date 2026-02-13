using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using EliteWhisper.Services;
using EliteWhisper.Messages;
using CommunityToolkit.Mvvm.Messaging;

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

        // Dynamic Card Properties
        public string CurrentMicName => _audioService.DefaultMicName;

        public System.Collections.Generic.IReadOnlyList<DictationMode> Modes => _modeService.Modes;

        public DictationMode SelectedMode
        {
            get => _modeService.ActiveMode;
            set
            {
                if (_modeService.ActiveMode != value)
                {
                    _modeService.SetActiveMode(value);
                    OnPropertyChanged();
                    RefreshModeInfo();
                }
            }
        }

        [ObservableProperty]
        private string _currentProviderName = "Auto";

        [ObservableProperty]
        private string _dictationButtonText = "Start Dictation";

        [ObservableProperty]
        private string _cardBorderColor = "#10B981"; // Green

        private readonly AIEngineService _aiEngine;
        private readonly AudioCaptureService _audioService;
        private readonly ModeService _modeService;
        private readonly LlmService _llmService;
        private readonly DictationService _dictationService;
        private readonly HistoryService _historyService;

        // Metrics
        public EliteWhisper.Controls.AnimatedCounter AverageWpmCounter { get; } = new();
        public EliteWhisper.Controls.AnimatedCounter WordsThisWeekCounter { get; } = new();
        public EliteWhisper.Controls.AnimatedCounter TotalWordsCounter { get; } = new();
        public EliteWhisper.Controls.AnimatedCounter AppsUsedCounter { get; } = new();
        public EliteWhisper.Controls.AnimatedCounter MinutesSavedCounter { get; } = new();

        [ObservableProperty]
        private double _wpmTrend;

        [ObservableProperty]
        private double _wordsTrend;

        public DashboardViewModel(
            AIEngineService aiEngine,
            AudioCaptureService audioService,
            ModeService modeService,
            LlmService llmService,
            DictationService dictationService,
            HistoryService historyService)
        {
            _aiEngine = aiEngine;
            _audioService = audioService;
            _modeService = modeService;
            _llmService = llmService;
            _dictationService = dictationService;
            _historyService = historyService;

            _aiEngine.StateChanged += OnEngineStateChanged;
            _modeService.ActiveModeChanged += OnModeChanged;
            
            // Listen for new records
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<Messages.RecordAddedMessage>(this, (r, m) =>
            {
               _ = RecalculateAsync();
            });

            // Initial load
            _ = RecalculateAsync();
            
            // Initialize
            UpdateStatus(_aiEngine.State);
            RefreshModeInfo();
        }

        private void OnEngineStateChanged(object? sender, EngineState state)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateStatus(state));
        }

        private void OnModeChanged(object? sender, DictationMode mode)
        {
            OnPropertyChanged(nameof(SelectedMode));
            RefreshModeInfo();
        }

        private void RefreshModeInfo()
        {
            var provider = _llmService.SelectProvider(_modeService.ActiveMode);
            CurrentProviderName = provider?.Name ?? "Auto";
        }

        private void UpdateStatus(EngineState state)
        {
            switch (state)
            {
                case EngineState.Idle:
                    AppStatus = "Idle";
                    StatusColor = "#6B7280"; // Gray
                    StatusMessage = "Engine not configured";
                    CardBorderColor = "#6B7280";
                    DictationButtonText = "Start Dictation";
                    break;
                case EngineState.Loading:
                    AppStatus = "Loading...";
                    StatusColor = "#F59E0B"; // Amber
                    StatusMessage = "Loading AI model";
                    CardBorderColor = "#F59E0B";
                    DictationButtonText = "Loading...";
                    break;
                case EngineState.Ready:
                    AppStatus = "Ready";
                    StatusColor = "#10B981"; // Green
                    StatusMessage = "Press F2 to dictate";
                    CardBorderColor = "#10B981";
                    DictationButtonText = "Start Dictation";
                    break;
                case EngineState.Recording:
                    // Only update visuals if the source is the App
                    if (_dictationService.CurrentSource == RecordingSource.App)
                    {
                        AppStatus = "Listening";
                        StatusColor = "#EF4444"; // Red
                        StatusMessage = "Recording audio...";
                        CardBorderColor = "#EF4444";
                        DictationButtonText = "Stop Recording";
                    }
                    else
                    {
                        // If Widget is recording, keep the Dashboard properly "Ready" looking
                        // so the button doesn't "activate" automatically.
                        AppStatus = "Ready";
                        StatusColor = "#10B981"; // Green
                        StatusMessage = "Press F2 to dictate";
                        CardBorderColor = "#10B981";
                        DictationButtonText = "Start Dictation";
                    }
                    break;
                case EngineState.Processing:
                    if (_dictationService.CurrentSource == RecordingSource.App)
                    {
                        AppStatus = "Thinking";
                        StatusColor = "#8B5CF6"; // Purple
                        StatusMessage = "Transcribing speech...";
                        CardBorderColor = "#8B5CF6";
                        DictationButtonText = "Processing...";
                    }
                    else
                    {
                        // Mask Widget processing state
                        AppStatus = "Ready";
                        StatusColor = "#10B981";
                        StatusMessage = "Press F2 to dictate";
                        CardBorderColor = "#10B981";
                        DictationButtonText = "Start Dictation";
                    }
                    break;
                case EngineState.Error:
                    AppStatus = "Error";
                    StatusColor = "#EF4444"; // Red
                    StatusMessage = "Check configuration";
                    CardBorderColor = "#EF4444";
                    DictationButtonText = "Retry";
                    break;
            }
        }

        [RelayCommand]
        private async Task ToggleDictation()
        {
            if (_aiEngine.State == EngineState.Recording)
            {
                // If it's a widget recording, we can still stop it (safety), 
                // or we could block it. Let's allow stopping for now but maybe the user wants it blocked?
                // The user said "app start dictate should only start recording when the user press the button".
                // Logic: If button says "Stop Recording" (App Source), stop it.
                // If button says "Recording (Widget)", providing a Stop is still useful.
                // But let's respect the "Toggle" nature.
                await _dictationService.StopListeningAndProcessAsync();
            }
            else if (_aiEngine.State == EngineState.Ready || _aiEngine.State == EngineState.Error)
            {
                // Start with App Source
                _dictationService.StartListening(RecordingSource.App);
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

        public bool IsModesPage
        {
            get => CurrentPage == AppPage.Modes;
            set { if (value) CurrentPage = AppPage.Modes; }
        }

        public bool IsAiProvidersPage
        {
            get => CurrentPage == AppPage.AiProviders;
            set { if (value) CurrentPage = AppPage.AiProviders; }
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
            OnPropertyChanged(nameof(IsModesPage));
            OnPropertyChanged(nameof(IsAiProvidersPage));
            OnPropertyChanged(nameof(IsConfigurationPage));
            OnPropertyChanged(nameof(IsSoundPage));
            OnPropertyChanged(nameof(IsHistoryPage));
            OnPropertyChanged(nameof(IsAboutPage));
        }
        public async Task RecalculateAsync()
        {
            // Snapshot on UI thread to avoid collection modification errors
            System.Collections.Generic.List<DictationRecord> records = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
            {
                records = _historyService.History.ToList();
            });

            if (records == null) return;

            await Task.Run(async () =>
            {
                try
                {
                    if (records.Count == 0) return;
                    
                    // 1. Total Words
                    int totalWords = records.Sum(r => r.WordCount);
                    
                    // 2. Average WPM
                    // Only consider records with valid duration > 0 and WordCount > 0
                    var validRecords = records.Where(r => r.DurationSeconds > 0 && r.WordCount > 0).ToList();
                    int avgWpm = 0;
                    if (validRecords.Any())
                    {
                        double totalDurationMin = validRecords.Sum(r => r.DurationSeconds) / 60.0;
                        if (totalDurationMin > 0.1)
                        {
                            avgWpm = (int)(validRecords.Sum(r => r.WordCount) / totalDurationMin);
                        }
                    }

                    // 3. Words This Week
                    var now = DateTime.Now;
                    var oneWeekAgo = now.AddDays(-7);
                    int wordsThisWeek = records.Where(r => r.Timestamp >= oneWeekAgo).Sum(r => r.WordCount);

                    // 4. Previous Week for Trend
                    var twoWeeksAgo = now.AddDays(-14);
                    int wordsPrevWeek = records.Where(r => r.Timestamp >= twoWeeksAgo && r.Timestamp < oneWeekAgo).Sum(r => r.WordCount);

                    // 5. Apps Used (Distinct)
                    int appsUsed = records
                        .Where(r => !string.IsNullOrEmpty(r.ApplicationName) && r.ApplicationName != "Unknown")
                        .Select(r => r.ApplicationName)
                        .Distinct()
                        .Count();

                    // 6. Minutes Saved
                    // Typing speed assumption: 40 WPM
                    // Dictation Time: Actual duration
                    // Saved = (Words / 40) - (Duration / 60)
                    double typingMinutes = totalWords / 40.0;
                    double dictationMinutes = records.Sum(r => r.DurationSeconds) / 60.0;
                    int minutesSaved = (int)Math.Max(0, typingMinutes - dictationMinutes);

                    // Trends
                    double wpmTrendVal = 0; 
                    // WPM trend is tricky, let's do Words Trend as requested
                    double wordsTrendVal = 0;
                    if (wordsPrevWeek > 0)
                    {
                        wordsTrendVal = ((double)(wordsThisWeek - wordsPrevWeek) / wordsPrevWeek) * 100;
                    }
                    else if (wordsThisWeek > 0)
                    {
                        wordsTrendVal = 100; // infinite growth
                    }

                    // For WPM Trend, compare this week vs last week average
                    double wpmThisWeek = 0;
                    var thisWeekRecords = records.Where(r => r.Timestamp >= oneWeekAgo && r.DurationSeconds > 0).ToList();
                    if (thisWeekRecords.Any())
                    {
                        wpmThisWeek = thisWeekRecords.Sum(r => r.WordCount) / (thisWeekRecords.Sum(r => r.DurationSeconds) / 60.0);
                    }

                    double wpmPrevWeek = 0;
                    var prevWeekRecords = records.Where(r => r.Timestamp >= twoWeeksAgo && r.Timestamp < oneWeekAgo && r.DurationSeconds > 0).ToList();
                    if (prevWeekRecords.Any())
                    {
                        wpmPrevWeek = prevWeekRecords.Sum(r => r.WordCount) / (prevWeekRecords.Sum(r => r.DurationSeconds) / 60.0);
                    }

                    if (wpmPrevWeek > 0)
                    {
                        wpmTrendVal = ((wpmThisWeek - wpmPrevWeek) / wpmPrevWeek) * 100;
                    }

                    // Update UI
                    System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                    {
                        WpmTrend = wpmTrendVal;
                        WordsTrend = wordsTrendVal;

                        // Animate
                        var t1 = AverageWpmCounter.AnimateTo(avgWpm);
                        var t2 = WordsThisWeekCounter.AnimateTo(wordsThisWeek);
                        var t3 = TotalWordsCounter.AnimateTo(totalWords);
                        var t4 = AppsUsedCounter.AnimateTo(appsUsed);
                        var t5 = MinutesSavedCounter.AnimateTo(minutesSaved);
                        
                        await Task.WhenAll(t1, t2, t3, t4, t5);
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Dashboard Recalculation Error: {ex}");
                }
            });
        }
    }
}
