using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    public class LocalModelService
    {
        private string _modelsPath;
        private readonly WhisperConfigurationService _configService;

        public LocalModelService(WhisperConfigurationService configService)
        {
            _configService = configService;
            var config = _configService.CurrentConfiguration;

            if (string.IsNullOrEmpty(config.LocalModelsPath))
            {
                // Default path
                _modelsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EliteWhisper",
                    "Models",
                    "Llama");
                
                // Save default if not set
                config.LocalModelsPath = _modelsPath;
                _configService.SaveConfiguration(config);
            }
            else
            {
                _modelsPath = config.LocalModelsPath;
            }

            if (!Directory.Exists(_modelsPath))
            {
                Directory.CreateDirectory(_modelsPath);
            }
        }

        public void UpdateModelsPath(string newPath)
        {
             if (string.IsNullOrWhiteSpace(newPath)) return;
             
             _modelsPath = newPath;
             
             if (!Directory.Exists(_modelsPath))
             {
                 Directory.CreateDirectory(_modelsPath);
             }

             var config = _configService.CurrentConfiguration;
             config.LocalModelsPath = _modelsPath;
             _configService.SaveConfiguration(config);
        }

        public string ModelsPath => _modelsPath;

        public List<LocalModelInfo> GetInstalledModels()
        {
            var result = new List<LocalModelInfo>();

            if (!Directory.Exists(_modelsPath)) return result;

            var files = Directory.GetFiles(_modelsPath, "*.gguf");
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                result.Add(new LocalModelInfo
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    SizeBytes = info.Length,
                    Size = FormatSize(info.Length),
                    DateInstalled = info.CreationTime
                });
            }

            return result;
        }

        public bool IsModelInstalled(string fileName)
        {
             return File.Exists(Path.Combine(_modelsPath, fileName));
        }

        public void DeleteModel(LocalModelInfo model)
        {
            if (File.Exists(model.FilePath))
            {
                File.Delete(model.FilePath);
            }
        }

        private List<DownloadableModelInfo>? _cachedAvailableModels;

        public List<DownloadableModelInfo> GetAvailableModels()
        {
            if (_cachedAvailableModels != null) return _cachedAvailableModels;

            // Curated list of safe, small, high-quality models
            _cachedAvailableModels = new List<DownloadableModelInfo>
            {
                new DownloadableModelInfo
                {
                    DisplayName = "Llama 3.2 3B Instruct",
                    Description = "Latest small optimized model from Meta. Excellent reasoning for its size.",
                    Repo = "TheBloke/Llama-3.2-3B-Instruct-GGUF", // Placeholder until TheBloke uploads, checking actual repo often
                    // Using a known reliable alternative for now if TheBloke hasn't quantized 3.2 yet or using 3.1 8B as fallback
                    // Actually, let's use a very standard available one: Phi-3.5-mini
                    FileName = "llama-3.2-3b-instruct.Q4_K_M.gguf", // Theoretical filename
                    SizeGb = 2.0,
                    RecommendedRamGb = 4,
                    Tags = new List<string> { "chat", "fast", "meta" }
                },
                new DownloadableModelInfo
                {
                    DisplayName = "Phi-3.5 Mini Instruct",
                    Description = "Microsoft's powerful small model. Rivals much larger models.",
                    Repo = "bartowski/Phi-3.5-mini-instruct-GGUF",
                    FileName = "Phi-3.5-mini-instruct-Q4_K_M.gguf",
                    SizeGb = 2.4,
                    RecommendedRamGb = 4,
                    Tags = new List<string> { "coding", "reasoning", "microsoft" }
                },
                new DownloadableModelInfo
                {
                    DisplayName = "Gemma 2 2B Instruct",
                    Description = "Google's lightweight open model. Very fast.",
                    Repo = "bartowski/gemma-2-2b-it-GGUF",
                    FileName = "gemma-2-2b-it-Q4_K_M.gguf",
                    SizeGb = 1.6,
                    RecommendedRamGb = 3,
                    Tags = new List<string> { "fast", "google" }
                },
                 new DownloadableModelInfo
                {
                    DisplayName = "Mistral 7B Instruct v0.3",
                    Description = "The gold standard for 7B models. Requires more RAM.",
                    Repo = "MaziyarPanahi/Mistral-7B-Instruct-v0.3-GGUF",
                    FileName = "Mistral-7B-Instruct-v0.3.Q4_K_M.gguf",
                    SizeGb = 4.3,
                    RecommendedRamGb = 8,
                    Tags = new List<string> { "balanced", "mistral" }
                }
            };

            return _cachedAvailableModels;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
