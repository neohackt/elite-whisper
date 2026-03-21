using System;
using System.Threading.Tasks;
using System.Windows;
using System.Net.Http;
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

        public IServiceProvider Services => AppHost!.Services;

        private WidgetWindow? _widgetWindow;
        private TrayIconService? _trayIcon;

        public App()
        {
            Log("App Constructor Started");
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            try 
            {
                Log("Configuring Host Builder...");
                AppHost = Host.CreateDefaultBuilder()
                    .ConfigureServices((hostContext, services) =>
                    {
                        try
                        {
                            Log("Registering Services...");
                            // Services
                            services.AddSingleton<HotkeyService>();
                            services.AddSingleton<TextInjectionService>();
                            services.AddSingleton<AudioCaptureService>();
                            services.AddSingleton<AudioPlayerService>();
                            services.AddSingleton<WhisperConfigurationService>();
                            services.AddSingleton<AIEngineService>();
                            services.AddSingleton<TrayIconService>();
                            services.AddSingleton<ModelRegistryService>();
                            services.AddSingleton<HistoryService>();
                            
                            // Local Models (Llama)
                            services.AddSingleton<ModelDownloadService>();
                            services.AddSingleton<LocalModelService>();
                            services.AddSingleton<LlamaCppService>();
                            services.AddSingleton<Services.LLM.LocalLlmProvider>();
                            
                            // LLM Services
                            services.AddHttpClient("Ollama", client =>
                            {
                                client.Timeout = TimeSpan.FromMinutes(5); // Increased for local inference
                            });
                            
                            services.AddHttpClient("Gemini", client =>
                            {
                                client.Timeout = TimeSpan.FromSeconds(60);
                            });
                            
                            services.AddHttpClient("OpenRouter", client =>
                            {
                                client.Timeout = TimeSpan.FromSeconds(60);
                            });

                            services.AddSingleton<Services.LLM.OllamaProvider>(sp =>
                            {
                                var factory = sp.GetRequiredService<IHttpClientFactory>();
                                return new Services.LLM.OllamaProvider(factory.CreateClient("Ollama"));
                            });
                            services.AddSingleton<Services.LLM.GeminiProvider>(sp =>
                            {
                                var factory = sp.GetRequiredService<IHttpClientFactory>();
                                var configService = sp.GetRequiredService<WhisperConfigurationService>();
                                return new Services.LLM.GeminiProvider(factory.CreateClient("Gemini"), configService);
                            });
                            services.AddSingleton<Services.LLM.OpenRouterProvider>(sp =>
                            {
                                var factory = sp.GetRequiredService<IHttpClientFactory>();
                                var configService = sp.GetRequiredService<WhisperConfigurationService>();
                                return new Services.LLM.OpenRouterProvider(factory.CreateClient("OpenRouter"), configService);
                            });


                            // Inject LocalLlmProvider into LlmService
                            services.AddSingleton<LlmService>(sp =>
                            {
                                var ollama = sp.GetRequiredService<Services.LLM.OllamaProvider>();
                                var gemini = sp.GetRequiredService<Services.LLM.GeminiProvider>();
                                var openRouter = sp.GetRequiredService<Services.LLM.OpenRouterProvider>();
                                var localLlm = sp.GetRequiredService<Services.LLM.LocalLlmProvider>();
                                var config = sp.GetRequiredService<WhisperConfigurationService>();
                                return new LlmService(ollama, gemini, openRouter, localLlm, config);
                            });
                            services.AddSingleton<PostProcessingService>();
                            services.AddSingleton<ModeService>();
                            
                            // Views and ViewModels
                            services.AddSingleton<MainWindow>();
                            services.AddSingleton<MainViewModel>();
                            services.AddSingleton<DashboardViewModel>();
                            services.AddTransient<LocalModelsViewModel>();
                            services.AddTransient<ModelsViewModel>();
                            services.AddTransient<ModesViewModel>(sp =>
                            {
                                var modeService = sp.GetRequiredService<ModeService>();
                                var ollamaProvider = sp.GetRequiredService<Services.LLM.OllamaProvider>();
                                var openRouterProvider = sp.GetRequiredService<Services.LLM.OpenRouterProvider>();
                                var configService = sp.GetRequiredService<WhisperConfigurationService>();
                                var localModelService = sp.GetRequiredService<LocalModelService>();
                                return new ModesViewModel(modeService, ollamaProvider, openRouterProvider, configService, localModelService);
                            });
                            services.AddTransient<AiProvidersViewModel>(sp =>
                            {
                                var configService = sp.GetRequiredService<WhisperConfigurationService>();
                                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                                var ollamaProvider = sp.GetRequiredService<Services.LLM.OllamaProvider>();
                                var geminiProvider = sp.GetRequiredService<Services.LLM.GeminiProvider>();
                                return new AiProvidersViewModel(configService, httpClientFactory, ollamaProvider, geminiProvider);
                            });
                            services.AddTransient<HistoryViewModel>();
                            services.AddTransient<SoundViewModel>();
                            services.AddTransient<ConfigurationViewModel>();
                            services.AddTransient<AboutViewModel>();
                            
                            // Widget
                            services.AddSingleton<WidgetWindow>();
                            services.AddSingleton<WidgetViewModel>();
                            
                            // Controllers
                            services.AddSingleton<DictationService>();
                            
                            // Speech Engines
                            services.AddSingleton<EliteWhisper.Services.Speech.HardwareDetectionService>();
                            services.AddSingleton<EliteWhisper.Services.Speech.SpeechEngineSelector>();
                            services.AddSingleton<EliteWhisper.Services.Speech.SpeechRecognitionService>();
                            
                            // Updates
                            services.AddSingleton<IUpdateService, UpdateService>();
                            Log("Services Registered Successfully");
                        }
                        catch (Exception ex)
                        {
                            Log($"Error Registering Services: {ex.Message}\n{ex.StackTrace}");
                            throw;
                        }
                    })
                    .Build();
                Log("Host Built Successfully");
            }
            catch (Exception ex)
            {
                 Log($"Startup Error (Host Construction): {ex.Message}\n{ex.StackTrace}");
                 MessageBox.Show($"Startup Error: {ex.Message}");
                 throw; 
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            Log("OnStartup Called");
            try
            {
                if (AppHost == null) 
                {
                     Log("AppHost is null!");
                     throw new InvalidOperationException("AppHost is not initialized.");
                }

                // check first run
                Log("Checking Configuration...");
                var configService = AppHost.Services.GetRequiredService<WhisperConfigurationService>();
                if (!configService.CurrentConfiguration.HasCompletedFirstRun)
                {
                    Log("First Run Detected - Showing Wizard");
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
                        Log("Wizard Cancelled - Shutting Down");
                        // User cancelled/closed without finishing
                        Shutdown();
                        return;
                    }
                }

                // Proceed with normal startup
                Log("Starting AppHost...");
                await AppHost!.StartAsync();
                
                // Start Update Service
                Log("Starting Update Service...");
                var updateService = AppHost.Services.GetRequiredService<IUpdateService>();
                updateService.Start();
                
                // Show Main Window immediately after Wizard (or on normal startup)
                Log("Showing Main Window...");
                var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                mainWindow.Activate();
                
                // Initialize tray icon
                Log("Initializing Tray Icon...");
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
                Log("Initializing Widget Window...");
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
                
                Log("Startup Complete");
                
                // Prime hardware profile in background to eliminate first-use WMI delay
                _ = Task.Run(() => {
                    try {
                        var hardware = AppHost.Services.GetRequiredService<EliteWhisper.Services.Speech.HardwareDetectionService>();
                        hardware.GetProfile();
                        Log("Hardware profile primed in background");
                    } catch (Exception ex) {
                        Log($"Hardware priming failed: {ex.Message}");
                    }
                });

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                Log($"Startup Critical Error: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Startup Error: {ex.Message}\n\n{ex.StackTrace}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            Log("OnExit Called");
            _trayIcon?.Dispose();
            if (AppHost != null)
            {
                await AppHost.StopAsync();
            }
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log($"Unhandled Exception: {e.Exception.Message}\n{e.Exception.StackTrace}");
            MessageBox.Show($"CRASH: {e.Exception.Message}\nStack: {e.Exception.StackTrace}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void Log(string message)
        {
            try
            {
                string logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.txt");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                System.IO.File.AppendAllText(logFile, $"[{timestamp}] {message}{Environment.NewLine}");
            }
            catch { /* Ignore logging errors */ }
        }
    }
}
