namespace EliteWhisper.Models
{
    /// <summary>
    /// Preferred LLM provider for post-processing.
    /// </summary>
    public enum PreferredProvider
    {
        /// <summary>
        /// Automatically select best available provider.
        /// Priority: Ollama (if RunLocally=true) → Gemini → OpenRouter
        /// </summary>
        Auto,
        
        /// <summary>
        /// Use local Ollama instance.
        /// </summary>
        Ollama,

        /// <summary>
        /// Legacy compatibility for Ollama.
        /// </summary>
        [System.Obsolete("Use Ollama instead.")]
        Local,
        
        /// <summary>
        /// Use built-in llama.cpp (Local)
        /// </summary>
        BuiltIn,
        
        /// <summary>
        /// Use Google Gemini API (free tier).
        /// </summary>
        Gemini,
        
        /// <summary>
        /// Use OpenRouter aggregator.
        /// </summary>
        OpenRouter
    }
}
