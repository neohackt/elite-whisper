using System.Threading;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services.LLM
{
    /// <summary>
    /// Interface for LLM providers (Ollama, Gemini, OpenRouter, etc.).
    /// </summary>
    public interface ILlmProvider
    {
        /// <summary>
        /// Provider name for display and logging.
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Whether this provider is currently available.
        /// For Ollama: checks if localhost:11434 is reachable.
        /// For cloud: checks if API key is configured.
        /// </summary>
        bool IsAvailable { get; }
        
        /// <summary>
        /// Generate text using this LLM provider.
        /// </summary>
        /// <param name="prompt">Input prompt/text to process.</param>
        /// <param name="options">Generation options (temperature, max tokens).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Generated text, or throws exception on failure.</returns>
        Task<string> GenerateAsync(
            string prompt,
            LlmOptions options,
            CancellationToken cancellationToken);
    }
}
