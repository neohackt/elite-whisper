using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    public class WhisperConfigurationService
    {
        private readonly string _configFilePath;
        private WhisperConfiguration _currentConfig;

        // Required DLLs that should exist next to the executable
        private static readonly string[] RequiredDlls = new[]
        {
            "ggml.dll",
            "whisper.dll"
        };

        // Optional DLLs (won't fail validation if missing)
        private static readonly string[] OptionalDlls = new[]
        {
            "ggml-base.dll",
            "ggml-cpu.dll",
            "SDL2.dll"
        };

        public WhisperConfiguration CurrentConfiguration => _currentConfig;

        public WhisperConfigurationService()
        {
            // Store config in AppData
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EliteWhisper");
            
            Directory.CreateDirectory(appDataPath);
            _configFilePath = Path.Combine(appDataPath, "whisper_config.json");
            
            _currentConfig = LoadConfiguration();
        }

        /// <summary>
        /// Discover and validate Whisper resources in a folder
        /// </summary>
        public WhisperValidationResult DiscoverAndValidate(string baseDirectory)
        {
            var result = new WhisperValidationResult();

            if (!Directory.Exists(baseDirectory))
            {
                result.Errors.Add($"Directory does not exist: {baseDirectory}");
                return result;
            }

            // 1. Find Whisper executable (prefer whisper-cli.exe)
            result.ExecutablePath = FindExecutable(baseDirectory);
            if (string.IsNullOrEmpty(result.ExecutablePath))
            {
                result.Errors.Add("No Whisper executable found (whisper-cli.exe or whisper.exe)");
            }

            // 2. Find model files
            result.AvailableModels = FindModelFiles(baseDirectory);
            if (result.AvailableModels.Count == 0)
            {
                result.Errors.Add("No model files found (ggml-*.bin)");
            }
            else
            {
                // Set models directory from first model found
                result.ModelsDirectory = Path.GetDirectoryName(result.AvailableModels[0]);
            }

            // 3. Validate DLLs next to executable
            if (!string.IsNullOrEmpty(result.ExecutablePath))
            {
                string exeDir = Path.GetDirectoryName(result.ExecutablePath)!;
                
                foreach (var dll in RequiredDlls)
                {
                    string dllPath = Path.Combine(exeDir, dll);
                    if (!File.Exists(dllPath))
                    {
                        result.Errors.Add($"Missing required DLL: {dll}");
                    }
                }

                foreach (var dll in OptionalDlls)
                {
                    string dllPath = Path.Combine(exeDir, dll);
                    if (!File.Exists(dllPath))
                    {
                        result.Warnings.Add($"Optional DLL not found: {dll}");
                    }
                }
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        /// <summary>
        /// Find Whisper executable in directory tree
        /// </summary>
        private string? FindExecutable(string baseDirectory)
        {
            // First try whisper-cli.exe (preferred)
            var cliExe = Directory.GetFiles(baseDirectory, "whisper-cli.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            
            if (!string.IsNullOrEmpty(cliExe))
                return cliExe;

            // Fallback to whisper.exe
            var whisperExe = Directory.GetFiles(baseDirectory, "whisper.exe", SearchOption.AllDirectories)
                .FirstOrDefault();

            return whisperExe;
        }

        /// <summary>
        /// Find all model files in directory tree
        /// </summary>
        private List<string> FindModelFiles(string baseDirectory)
        {
            try
            {
                return Directory.GetFiles(baseDirectory, "ggml-*.bin", SearchOption.AllDirectories)
                    .OrderBy(f => new FileInfo(f).Length) // Sort by size (smallest first)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Apply and save a validated configuration
        /// </summary>
        public void ApplyConfiguration(string baseDirectory, WhisperValidationResult validation)
        {
            if (!validation.IsValid)
                throw new InvalidOperationException("Cannot apply invalid configuration");

            // Capture existing user preferences to preserve them
            var existingConfig = _currentConfig;

            _currentConfig = new WhisperConfiguration
            {
                BaseDirectory = baseDirectory,
                ExecutablePath = validation.ExecutablePath,
                ModelsDirectory = validation.ModelsDirectory,
                DefaultModelPath = validation.AvailableModels.FirstOrDefault(),
                AvailableModels = validation.AvailableModels,
                LastValidated = DateTime.Now,

                // Restore persistent user settings
                HasCompletedFirstRun = existingConfig.HasCompletedFirstRun,
                HistoryStoragePath = existingConfig.HistoryStoragePath,
                Modes = existingConfig.Modes ?? new List<DictationMode>(),
                ActiveModeId = existingConfig.ActiveModeId,
                GeminiApiKey = existingConfig.GeminiApiKey,
                OpenRouterApiKey = existingConfig.OpenRouterApiKey,
                DefaultProviderPreference = existingConfig.DefaultProviderPreference
            };

            // If we have a custom models directory set in the wizard (that might differ from auto-discovery),
            // and the user hasn't just reset everything, we might want to respect it. 
            // However, validation result dictates what's actually available. 
            // For now, determining valid models takes precedence to ensure app works.

            SaveConfiguration(_currentConfig);
        }

        /// <summary>
        /// Set the default model to use
        /// </summary>
        public void SetDefaultModel(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Model file not found", modelPath);

            _currentConfig.DefaultModelPath = modelPath;
            SaveConfiguration(_currentConfig);
        }

        /// <summary>
        /// Set the custom history storage path
        /// </summary>
        public void SetHistoryStoragePath(string path)
        {
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Cannot access or create directory: {path}", ex);
                }
            }

            _currentConfig.HistoryStoragePath = path;
            SaveConfiguration(_currentConfig);
        }

        /// <summary>
        /// Load configuration from disk
        /// </summary>
        private WhisperConfiguration LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<WhisperConfiguration>(json);
                    
                    if (config != null)
                    {
                        // Re-discover models in case files changed
                        if (!string.IsNullOrEmpty(config.BaseDirectory) && Directory.Exists(config.BaseDirectory))
                        {
                            config.AvailableModels = FindModelFiles(config.BaseDirectory);
                        }
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            }

            return new WhisperConfiguration();
        }

        /// <summary>
        /// Save configuration to disk
        /// </summary>
        public void SaveConfiguration(WhisperConfiguration config)
        {
            try
            {
                // Ensure keys are encrypted before saving
                EncryptApiKeys(config);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
            }
        }

        private void EncryptApiKeys(WhisperConfiguration config)
        {
            try
            {
                if (!string.IsNullOrEmpty(config.GeminiApiKey) && !IsBase64String(config.GeminiApiKey))
                {
                    config.GeminiApiKey = EncryptString(config.GeminiApiKey);
                }

                if (!string.IsNullOrEmpty(config.OpenRouterApiKey) && !IsBase64String(config.OpenRouterApiKey))
                {
                    config.OpenRouterApiKey = EncryptString(config.OpenRouterApiKey);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Encryption failed: {ex.Message}");
            }
        }

        public string? DecryptApiKey(string? encryptedKey)
        {
            if (string.IsNullOrEmpty(encryptedKey)) return null;
            
            try
            {
                // If it's not base64, assume it's legacy plaintext
                if (!IsBase64String(encryptedKey))
                {
                    // It will get encrypted on next save
                    System.Diagnostics.Debug.WriteLine("Migrated legacy API key to encrypted storage");
                    return encryptedKey; 
                }

                return DecryptString(encryptedKey);
            }
            catch
            {
                // Decryption failed (maybe machine changed?), return empty
                return null;
            }
        }

        private string EncryptString(string plainText)
        {
            byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(
                plainBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }

        private string DecryptString(string encryptedText)
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
            byte[] plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                encryptedBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }

        private bool IsBase64String(string s)
        {
            Span<byte> buffer = new Span<byte>(new byte[s.Length]);
            return Convert.TryFromBase64String(s, buffer, out _);
        }

        /// <summary>
        /// Re-validate current configuration
        /// </summary>
        public bool RevalidateCurrentConfiguration()
        {
            if (string.IsNullOrEmpty(_currentConfig.BaseDirectory))
                return false;

            var result = DiscoverAndValidate(_currentConfig.BaseDirectory);
            if (result.IsValid)
            {
                ApplyConfiguration(_currentConfig.BaseDirectory, result);
            }
            return result.IsValid;
        }

        /// <summary>
        /// Get human-readable model name
        /// </summary>
        public static string GetModelDisplayName(string modelPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(modelPath);
            
            return fileName switch
            {
                "ggml-tiny" => "Tiny (Fast)",
                "ggml-tiny.en" => "Tiny English (Fast)",
                "ggml-base" => "Base (Balanced)",
                "ggml-base.en" => "Base English (Balanced)",
                "ggml-small" => "Small (Accurate)",
                "ggml-small.en" => "Small English (Accurate)",
                "ggml-medium" => "Medium (High Quality)",
                "ggml-medium.en" => "Medium English (High Quality)",
                "ggml-large" => "Large (Best Quality)",
                _ => fileName
            };
        }
    }
}
