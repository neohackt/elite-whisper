namespace EliteWhisper.Models
{
    /// <summary>
    /// Dictation mode with ASR settings and optional LLM post-processing.
    /// </summary>
    public class DictationMode
    {
        /// <summary>
        /// Unique identifier (e.g., "default", "email", "meeting").
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Display name for UI.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Description of what this mode does.
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        // ASR Behavior
        
        /// <summary>
        /// AI model to use for transcription ("fast", "balanced", "accurate").
        /// </summary>
        public string ModelId { get; set; } = "balanced";
        
        /// <summary>
        /// Enable automatic punctuation in transcription.
        /// </summary>
        public bool AutoPunctuation { get; set; } = true;
        
        /// <summary>
        /// Enable smart formatting (numbers, dates, etc.).
        /// </summary>
        public bool SmartFormatting { get; set; } = true;
        
        // Post-Processing
        
        /// <summary>
        /// Enable LLM post-processing for this mode.
        /// </summary>
        public bool EnablePostProcessing { get; set; }
        
        /// <summary>
        /// Post-processing configuration. Null if EnablePostProcessing is false.
        /// </summary>
        public PostProcessProfile? PostProcess { get; set; }
        
        // UX / Monetization
        
        /// <summary>
        /// Whether this is the default mode.
        /// </summary>
        public bool IsDefault { get; set; }
        
        /// <summary>
        /// Whether this mode requires Pro license (visual only, no enforcement yet).
        /// </summary>
        public bool IsPro { get; set; }
    }
}
