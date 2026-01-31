using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EliteWhisper.Views;
using EliteWhisper.ViewModels;
using EliteWhisper.Services;

namespace EliteWhisper
{
    public partial class App : Application
    {
        public static IHost? AppHost { get; private set; }

        private WidgetWindow? _widgetWindow;
        private TrayIconService? _trayIcon;

        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    // Services
                    services.AddSingleton<HotkeyService>();
                    services.AddSingleton<TextInjectionService>();
                    services.AddSingleton<AudioCaptureService>();
                    services.AddSingleton<WhisperConfigurationService>();
                    services.AddSingleton<AIEngineService>();
                    services.AddSingleton<TrayIconService>();
                    services.AddSingleton<ModelRegistryService>();
                    
                    // Views and ViewModels
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<DashboardViewModel>();
                    services.AddTransient<ModelsViewModel>();
                    
                    // Widget
                    services.AddSingleton<WidgetWindow>();
                    services.AddSingleton<WidgetViewModel>();
                    
                    // Controllers
                    services.AddSingleton<DictationService>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // check first run
                var configService = AppHost.Services.GetRequiredService<WhisperConfigurationService>();
                if (!configService.CurrentConfiguration.HasCompletedFirstRun)
                {
                    // Show Wizard Modally
                    var wizard = new Views.WizardWindow();
                    var vm = new ViewModels.WizardViewModel(configService, () => 
                    {
                        wizard.DialogResult = true;
                        wizard.Close();
                    });
                    wizard.DataContext = vm;
                    
                    if (wizard.ShowDialog() != true) 
                    {
                        // User cancelled/closed without finishing
                        Shutdown();
                        return;
                    }
                }

                // Proceed with normal startup
                await AppHost!.StartAsync();
                
                // Show Main Window immediately after Wizard (or on normal startup)
                var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                mainWindow.Activate();
                
                // Initialize tray icon
                _trayIcon = AppHost.Services.GetRequiredService<TrayIconService>();
                _trayIcon.Initialize();
                
                // Wire tray events
                _trayIcon.SettingsRequested += (s, args) =>
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                };
                
                _trayIcon.ExitRequested += (s, args) =>
                {
                    // Properly shut down
                    Shutdown();
                };
                
                _trayIcon.ShowWidgetRequested += (s, args) =>
                {
                    if (_widgetWindow != null)
                    {
                        _widgetWindow.Show();
                        _widgetWindow.Activate();
                    }
                };
                
                // Initialize widget window
                _widgetWindow = AppHost.Services.GetRequiredService<WidgetWindow>();
                _widgetWindow.Show();
                
                // Check if Whisper is configured, show balloon if not
                configService = AppHost.Services.GetRequiredService<WhisperConfigurationService>();
                if (!configService.CurrentConfiguration.IsConfigured)
                {
                    _trayIcon.ShowBalloon("Elite Whisper", 
                        "Whisper is not configured. Right-click tray icon → Settings to set up.", 
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
                
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup Error: {ex.Message}\n\n{ex.StackTrace}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            await AppHost!.StopAsync();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"CRASH: {e.Exception.Message}\nStack: {e.Exception.StackTrace}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
