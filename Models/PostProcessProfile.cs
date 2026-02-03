namespace EliteWhisper.Models
{
    /// <summary>
    /// Post-processing profile configuration for LLM enhancement.
    /// </summary>
    public class PostProcessProfile
    {
        /// <summary>
        /// Prompt template with {{text}} placeholder for raw transcription.
        /// </summary>
        public string PromptTemplate { get; set; } = string.Empty;

        /// <summary>
        /// Base prompt part (for UI separation).
        /// </summary>
        public string BasePromptTemplate { get; set; } = string.Empty;

        /// <summary>
        /// User extension to prompt (for future use).
        /// </summary>
        public string? UserPromptExtension { get; set; }
        
        /// <summary>
        /// Prefer local LLM (Ollama) over cloud providers.
        /// </summary>
        [System.Obsolete("Use PreferredProvider = PreferredProvider.Local instead.")]
        public bool RunLocally { get; set; }
        
        /// <summary>
        /// Preferred LLM provider. If unavailable, falls back to Auto logic.
        /// </summary>
        public PreferredProvider PreferredProvider { get; set; } = PreferredProvider.Auto;

        /// <summary>
        /// Preferred specific model ID (e.g. "phi3:mini").
        /// Only used if provider supports model selection.
        /// </summary>
        public string? PreferredModel { get; set; }
    }
}
