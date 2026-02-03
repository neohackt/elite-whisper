namespace EliteWhisper.Models
{
    /// <summary>
    /// Options for LLM text generation.
    /// </summary>
    public class LlmOptions
    {
        /// <summary>
        /// Controls randomness. Lower = more deterministic.
        /// Range: 0.0 to 1.0
        /// </summary>
        public float Temperature { get; set; } = 0.2f;
        
        /// <summary>
        /// Maximum number of tokens to generate.
        /// </summary>
        public int MaxTokens { get; set; } = 512;

        /// <summary>
        /// Specific model ID (e.g. "phi3:mini").
        /// </summary>
        public string? ModelId { get; set; }
    }
}
