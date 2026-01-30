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
