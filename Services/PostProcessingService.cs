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
                DebugHelper.Log($"[PostProcessing] Disabled for mode {mode.Id} (EnablePostProcessing: {mode.EnablePostProcessing})");
                Debug.WriteLine("[PostProcessing] Disabled for this mode, returning raw text");
                return rawText;
            }
            
            // Select provider
            var provider = _llmService.SelectProvider(mode);
            if (provider == null)
            {
                DebugHelper.Log("[PostProcessing] No provider available!");
                Debug.WriteLine("[PostProcessing] No provider available, returning raw text");
                return rawText;
            }
            
            Debug.WriteLine($"[PostProcessing] Using provider: {provider.Name}");
            DebugHelper.Log($"[PostProcessing] Selected Provider: {provider.Name}");
            
            try
            {
                DebugHelper.Log($"[PostProcessing] Starting process. Mode: {mode.Id}, Provider: {mode.PostProcess?.PreferredProvider}");

                // Build prompt from template
                var prompt = mode.PostProcess!.PromptTemplate.Replace("{{text}}", rawText);
                
                // Generate enhanced text
                // Resolve Model ID based on provider
                string? targetModelId = null;
                if (provider.Name == "Local (Ollama)") targetModelId = mode.PostProcess.PreferredLocalModel;
                else if (provider.Name == "Gemini") targetModelId = mode.PostProcess.PreferredGeminiModel;
                else if (provider.Name == "OpenRouter") targetModelId = mode.PostProcess.PreferredOpenRouterModel;
                
                // Fallback to legacy field if specific one is unset (migration support)
                if (string.IsNullOrEmpty(targetModelId)) targetModelId = mode.PostProcess.PreferredModel;

                var options = new LlmOptions
                {
                    Temperature = 0.2f,
                    MaxTokens = 4096, // Increased to allow reasoning models (DeepSeek-R1) to finish thinking
                    ModelId = targetModelId
                };
                
                DebugHelper.Log("[PostProcessing] Calling GenerateAsync...");
                Debug.WriteLine($"[PostProcessing] Prompt (First 100 chars): {prompt.Substring(0, Math.Min(prompt.Length, 100))}...");
                
                var enhancedText = await provider.GenerateAsync(prompt, options, default);
                
                DebugHelper.Log($"[PostProcessing] Success. Original length: {rawText.Length}, Enhanced length: {enhancedText.Length}");
                Debug.WriteLine($"[PostProcessing] Result (First 100 chars): {(enhancedText ?? "").Substring(0, Math.Min((enhancedText ?? "").Length, 100))}...");
                
                // Warn about empty results
                if (string.IsNullOrWhiteSpace(enhancedText))
                {
                    Debug.WriteLine("[PostProcessing] WARNING: Enhanced text is empty!");
                    DebugHelper.Log("[PostProcessing] WARNING: Enhanced text is empty!");
                }
                
                return string.IsNullOrWhiteSpace(enhancedText) ? rawText : enhancedText;
            }
            catch (EliteWhisper.Services.LLM.LlmRateLimitException)
            {
                DebugHelper.Log($"[PostProcessing] RATE LIMITED (Provider: {provider.Name}). Attempting fallback...");
                
                // Fallback 1: Gemini
                if (provider.Name != "Gemini" && _llmService.GeminiProvider?.IsAvailable == true)
                {
                    try
                    {
                        DebugHelper.Log("[PostProcessing] Fallback to Gemini...");
                        // Use Gemini with same prompt
                         // Build prompt from template (re-use)
                        var prompt = mode.PostProcess.PromptTemplate.Replace("{{text}}", rawText);
                        var options = new LlmOptions { Temperature = 0.2f, MaxTokens = 512 }; // Default options for fallback
                        return await _llmService.GeminiProvider.GenerateAsync(prompt, options, default);
                    }
                    catch (Exception ex)
                    {
                        DebugHelper.Log($"[PostProcessing] Fallback Gemini Failed: {ex.Message}");
                    }
                }
                
                // Fallback 2: Local
                if (provider.Name != "Local" && _llmService.OllamaProvider?.IsAvailable == true)
                {
                    try
                    {
                        DebugHelper.Log("[PostProcessing] Fallback to Local...");
                         // Build prompt from template (re-use)
                        var prompt = mode.PostProcess.PromptTemplate.Replace("{{text}}", rawText);
                        var options = new LlmOptions { Temperature = 0.2f, MaxTokens = 512 };
                        return await _llmService.OllamaProvider.GenerateAsync(prompt, options, default);
                    }
                    catch (Exception ex)
                    {
                         DebugHelper.Log($"[PostProcessing] Fallback Local Failed: {ex.Message}");
                    }
                }
                
                // If all fail, return raw text
                DebugHelper.Log("[PostProcessing] All fallbacks failed. Returning raw text.");
                // User requested: "Cloud model busy — using local AI" toast. 
                // Since we don't have a toast service readily injected here, we just silently return raw text for now or log it.
                return rawText;
            }
            catch (TaskCanceledException)
            {
                DebugHelper.Log($"[PostProcessing] TIMEOUT (Provider: {provider.Name}). Returning raw text.");
                Debug.WriteLine("[PostProcessing] TIMEOUT. Returning raw text used.");
                // Silent failure for timeout - likely local model taking too long or disconnected
                return rawText;
            }
            catch (Exception ex)
            {
                // CRITICAL: Never break dictation due to LLM errors
                DebugHelper.Log($"[PostProcessing] FAILED: {ex.Message}");
                DebugHelper.Log($"[PostProcessing] Stack: {ex.StackTrace}");
                Debug.WriteLine($"[PostProcessing] Failed: {ex.Message}");
                return rawText;
            }
        }
    }
}
