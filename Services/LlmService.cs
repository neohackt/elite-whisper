using System.Collections.Generic;
using System.Linq;
using EliteWhisper.Models;
using EliteWhisper.Services.LLM;

namespace EliteWhisper.Services
{
    /// <summary>
    /// Manages LLM providers and handles provider selection/routing.
    /// </summary>
    public class LlmService
    {
        private readonly List<ILlmProvider> _providers = new();
        
        public ILlmProvider? OllamaProvider { get; private set; }
        public ILlmProvider? GeminiProvider { get; private set; }
        public ILlmProvider? OpenRouterProvider { get; private set; }
        public ILlmProvider? LocalLlmProvider { get; private set; }
        
        private readonly WhisperConfigurationService _configService;

        public LlmService(
            OllamaProvider ollama,
            GeminiProvider gemini,
            OpenRouterProvider openRouter,
            LocalLlmProvider localLlm,
            WhisperConfigurationService configService)
        {
            OllamaProvider = ollama;
            GeminiProvider = gemini;
            OpenRouterProvider = openRouter;
            LocalLlmProvider = localLlm;
            _configService = configService;
            
            _providers.Add(ollama);
            _providers.Add(gemini);
            _providers.Add(openRouter);
            _providers.Add(localLlm);
        }
        
        /// <summary>
        /// Select the best available provider based on mode preferences.
        /// </summary>
        /// <param name="mode">Dictation mode with provider preferences.</param>
        /// <returns>Available provider, or null if none available.</returns>
        public ILlmProvider? SelectProvider(DictationMode mode)
        {
            if (mode.PostProcess == null)
                return null;
            
            var preferredProvider = mode.PostProcess.PreferredProvider;
            
            // Try preferred provider first
            switch (preferredProvider)
            {
                case PreferredProvider.BuiltIn:
                    if (LocalLlmProvider?.IsAvailable == true) 
                    {
                        // Ensure provider knows which model to use
                        if (LocalLlmProvider is Services.LLM.LocalLlmProvider localProvider)
                        {
                            localProvider.SetPreferredModel(mode.PostProcess.PreferredBuiltInModel);
                        }
                        return LocalLlmProvider;
                    }
                    break;

                case PreferredProvider.Ollama:
                #pragma warning disable CS0612 // Type or member is obsolete
                case PreferredProvider.Local:
                #pragma warning restore CS0612
                    if (OllamaProvider?.IsAvailable == true) return OllamaProvider;
                    break;
                    
                case PreferredProvider.Gemini:
                    if (GeminiProvider?.IsAvailable == true)
                        return GeminiProvider;
                    break;
                    
                case PreferredProvider.OpenRouter:
                    if (OpenRouterProvider?.IsAvailable == true)
                        return OpenRouterProvider;
                    break;
            }
            
            // Auto fallback logic (also used if preferred provider is unavailable)
            // Priority: Local (if available) → Gemini → OpenRouter
            
            // Check Local first (Free, Privacy-first)
            // Start check based on preference
            if (_configService.CurrentConfiguration.LocalEngine == LocalAiEngine.BuiltIn)
            {
                 if (LocalLlmProvider?.IsAvailable == true) return LocalLlmProvider;
            }
            else
            {
                if (OllamaProvider?.IsAvailable == true) return OllamaProvider;
            }
            
            // Then Gemini (Fast, Free Tier)
            if (GeminiProvider?.IsAvailable == true)
                return GeminiProvider;
            
            // Finally OpenRouter (Paid/Flexible)
            if (OpenRouterProvider?.IsAvailable == true)
                return OpenRouterProvider;
            
            // Last resort: any available provider
            return _providers.FirstOrDefault(p => p.IsAvailable);
        }
        
        /// <summary>
        /// Get all available providers.
        /// </summary>
        public IEnumerable<ILlmProvider> GetAvailableProviders()
        {
            return _providers.Where(p => p.IsAvailable);
        }
    }
}
