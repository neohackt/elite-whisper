using System.Text.Json.Serialization;

namespace EliteWhisper.Models
{
    public class WhisperConfiguration
    {
        /// <summary>
        /// Path to the Whisper executable (whisper-cli.exe or whisper.exe)
        /// </summary>
        public string? ExecutablePath { get; set; }

        /// <summary>
        /// Directory containing the model files
        /// </summary>
        public string? ModelsDirectory { get; set; }

        /// <summary>
        /// Currently selected model file (ggml-*.bin)
        /// </summary>
        public string? DefaultModelPath { get; set; }

        /// <summary>
        /// Base folder selected by user
        /// </summary>
        public string? BaseDirectory { get; set; }

        /// <summary>
        /// Custom path for storing dictation history
        /// </summary>
        public string? HistoryStoragePath { get; set; }

        /// <summary>
        /// Last validation timestamp
        /// </summary>
        public DateTime? LastValidated { get; set; }

        /// <summary>
        /// Check if configuration is complete
        /// </summary>
        [JsonIgnore]
        public bool IsConfigured => 
            !string.IsNullOrEmpty(ExecutablePath) && 
            !string.IsNullOrEmpty(DefaultModelPath) &&
            System.IO.File.Exists(ExecutablePath) &&
            System.IO.File.Exists(DefaultModelPath);

        /// <summary>
        /// Flag to indicate if the First-Run Wizard has been completed
        /// </summary>
        public bool HasCompletedFirstRun { get; set; } = false;
        
        // Dictation Modes
        
        /// <summary>
        /// Available dictation modes with post-processing settings.
        /// </summary>
        public List<DictationMode> Modes { get; set; } = new();
        
        /// <summary>
        /// Currently active mode ID.
        /// </summary>
        public string? ActiveModeId { get; set; }
        
        // LLM API Keys
        
        /// <summary>
        /// Google Gemini API key for cloud post-processing.
        /// </summary>
        public string? GeminiApiKey { get; set; }
        
        /// <summary>
        /// OpenRouter API key for aggregated LLM access.
        /// </summary>
        public string? OpenRouterApiKey { get; set; }

        /// <summary>
        /// Default preference for LLM provider.
        /// </summary>
        public PreferredProvider DefaultProviderPreference { get; set; } = PreferredProvider.Auto;

        /// <summary>
        /// Available models discovered
        /// </summary>
        [JsonIgnore]
        public List<string> AvailableModels { get; set; } = new();
    }

    public class WhisperValidationResult
    {
        public bool IsValid { get; set; }
        public string? ExecutablePath { get; set; }
        public string? ModelsDirectory { get; set; }
        public List<string> AvailableModels { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
