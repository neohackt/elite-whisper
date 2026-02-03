using System;
using System.Diagnostics;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    /// <summary>
    /// Handles post-processing of transcriptions using LLMs.
    /// FAIL-SAFE: Always returns text, never throws exceptions.
    /// </summary>
    public class PostProcessingService
    {
        private readonly LlmService _llmService;
        
        public PostProcessingService(LlmService llmService)
        {
            _llmService = llmService;
        }
        
        /// <summary>
        /// Process raw transcription text with LLM if enabled.
        /// CRITICAL: This method NEVER throws exceptions. If LLM fails, returns raw text.
        /// </summary>
        /// <param name="rawText">Raw transcription from ASR engine.</param>
        /// <param name="mode">Active dictation mode.</param>
        /// <returns>Enhanced text if successful, or raw text if LLM unavailable/fails.</returns>
        public async Task<string> ProcessAsync(string rawText, DictationMode mode)
        {
            // Skip if post-processing disabled
            if (!mode.EnablePostProcessing || mode.PostProcess == null)
            {
                Debug.WriteLine("[PostProcessing] Disabled for this mode, returning raw text");
                return rawText;
            }
            
            // Select provider
            var provider = _llmService.SelectProvider(mode);
            if (provider == null)
            {
                Debug.WriteLine("[PostProcessing] No provider available, returning raw text");
                return rawText;
            }
            
            Debug.WriteLine($"[PostProcessing] Using provider: {provider.Name}");
            
            try
            {
                // Build prompt from template
                var prompt = mode.PostProcess.PromptTemplate.Replace("{{text}}", rawText);
                
                // Generate enhanced text
                var options = new LlmOptions
                {
                    Temperature = 0.2f,
                    MaxTokens = 512
                };
                
                var enhancedText = await provider.GenerateAsync(prompt, options, default);
                
                Debug.WriteLine($"[PostProcessing] Success. Original length: {rawText.Length}, Enhanced length: {enhancedText.Length}");
                
                return string.IsNullOrWhiteSpace(enhancedText) ? rawText : enhancedText;
            }
            catch (Exception ex)
            {
                // CRITICAL: Never break dictation due to LLM errors
                Debug.WriteLine($"[PostProcessing] Failed: {ex.Message}");
                Debug.WriteLine("[PostProcessing] Returning raw text (fail-safe)");
                return rawText;
            }
        }
    }
}
