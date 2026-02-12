using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using EliteWhisper.Models;

namespace EliteWhisper.Services.LLM
{
    /// <summary>
    /// LLM provider for OpenRouter API (aggregator).
    /// Endpoint: https://openrouter.ai/api/v1/chat/completions
    /// </summary>
    public class OpenRouterProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly WhisperConfigurationService _configService;
        private const string BaseUrl = "https://openrouter.ai/api/v1/chat/completions";
        private const string DefaultModel = "stepfun/step-3.5-flash:free"; // New Safe Default
        
        public string Name => "OpenRouter";
        
        public bool IsAvailable => !string.IsNullOrEmpty(_configService.CurrentConfiguration.OpenRouterApiKey);
        
        public OpenRouterProvider(HttpClient httpClient, WhisperConfigurationService configService)
        {
            _httpClient = httpClient;
            _configService = configService;
        }
        
        public async Task<string> GenerateAsync(
            string prompt,
            LlmOptions options,
            CancellationToken cancellationToken)
        {
            var encryptedKey = _configService.CurrentConfiguration.OpenRouterApiKey;
            if (string.IsNullOrEmpty(encryptedKey))
                throw new InvalidOperationException("OpenRouter API key is not configured.");

            var apiKey = _configService.DecryptApiKey(encryptedKey);
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Failed to decrypt OpenRouter API key.");
            
            var modelId = !string.IsNullOrEmpty(options.ModelId) ? options.ModelId : DefaultModel;
            
            // Defensive Check: OpenRouter models must contain "/"
            if (!modelId.Contains("/"))
            {
                 // Auto-fix or throw? User might have old setting.
                 // Let's try to default to something safe if invalid.
                 modelId = DefaultModel; 
            }

            async Task<string> ExecuteRequestAsync(string targetModel, CancellationToken ct)
            {
                var requestBody = new
                {
                    model = targetModel,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = options.Temperature,
                    max_tokens = options.MaxTokens,
                    // Optional: Add provider preferences here if needed
                };
                
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Add("HTTP-Referer", "elite-whisper"); 
                request.Headers.Add("X-Title", "Elite Whisper"); 
                
                var response = await _httpClient.SendAsync(request, ct);
                
                if (!response.IsSuccessStatusCode)
                {
                     var errorBody = await response.Content.ReadAsStringAsync();
                     System.Diagnostics.Debug.WriteLine($"[OpenRouter] Error {response.StatusCode} for {targetModel}: {errorBody}");
                     
                     if ((int)response.StatusCode == 429 || 
                         response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                     {
                         throw new LlmRateLimitException(errorBody);
                     }

                     throw new HttpRequestException($"OpenRouter API Error {response.StatusCode}: {errorBody}");
                }
                
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<OpenRouterResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                return result?.Choices?[0]?.Message?.Content ?? string.Empty;
            }

            try
            {
                return await ExecuteRequestAsync(modelId, cancellationToken);
            }
            catch (LlmRateLimitException)
            {
                // Fallback Logic: Retry with Free Recommended model if strictly needed
                string fallbackModel = DefaultModel;
                
                if (modelId != fallbackModel)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenRouter] Rate limit on {modelId}. Retrying with {fallbackModel}...");
                    try
                    {
                        return await ExecuteRequestAsync(fallbackModel, cancellationToken);
                    }
                    catch
                    {
                        throw; // If fallback fails, throw original
                    }
                }
                throw;
            }
        }

        public async Task<System.Collections.Generic.List<OpenRouterModelOption>> GetAvailableModelsAsync()
        {
            var encryptedKey = _configService.CurrentConfiguration.OpenRouterApiKey;
            if (string.IsNullOrEmpty(encryptedKey)) return new System.Collections.Generic.List<OpenRouterModelOption>();

            var apiKey = _configService.DecryptApiKey(encryptedKey);
            if (string.IsNullOrEmpty(apiKey)) return new System.Collections.Generic.List<OpenRouterModelOption>();

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Add("HTTP-Referer", "elite-whisper");
                request.Headers.Add("X-Title", "Elite Whisper");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OpenRouterModelListResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var list = new System.Collections.Generic.List<OpenRouterModelOption>();
                
                if (result?.Data != null)
                {
                    // Index API models by ID for fast lookup
                    var apiModels = result.Data.ToDictionary(m => m.Id.ToLowerInvariant(), m => m);

                    // Define Desired Models (Strict List to prevent duplicates)
                    // The key is the substring or exact ID we are looking for. 
                    // We will prioritize exact match, then StartsWith.
                    var strictList = new[] 
                    {
                        // Tier: Free (MUST have :free suffix in OpenRouter to be actually free)
                        new { SearchId = "stepfun/step-3.5-flash:free", Label = "Step 3.5 Flash (Fast)", Tier = OpenRouterModelTier.Free },
                        new { SearchId = "liquid/lfm-2.5-1.2b-instruct:free", Label = "LFM 2.5 Instruct (Structured)", Tier = OpenRouterModelTier.Free },
                        new { SearchId = "meta-llama/llama-3.2-3b-instruct:free", Label = "Llama 3.2 3B (Balanced)", Tier = OpenRouterModelTier.Free },
                        new { SearchId = "meta-llama/llama-3.3-70b-instruct:free", Label = "Llama 3.3 70B (Powerful)", Tier = OpenRouterModelTier.Free },
                        
                        // Tier: Free Advanced (MUST have :free suffix)
                        new { SearchId = "openai/gpt-oss-20b:free", Label = "GPT-OSS 20B", Tier = OpenRouterModelTier.FreeAdvanced },
                        new { SearchId = "openai/gpt-oss-120b:free", Label = "GPT-OSS 120B (Advanced)", Tier = OpenRouterModelTier.FreeAdvanced },
                        new { SearchId = "deepseek/deepseek-r1-0528:free", Label = "DeepSeek R1 (Reasoning)", Tier = OpenRouterModelTier.FreeAdvanced },
                        
                        // Tier: Paid Recommended
                        new { SearchId = "anthropic/claude-3-haiku", Label = "Claude 3 Haiku", Tier = OpenRouterModelTier.Recommended },
                        new { SearchId = "openai/gpt-4o-mini", Label = "GPT-4o Mini", Tier = OpenRouterModelTier.Recommended },
                        new { SearchId = "mistralai/mistral-small", Label = "Mistral Small", Tier = OpenRouterModelTier.Recommended },
                        
                        // Tier: Paid Advanced
                        new { SearchId = "anthropic/claude-3-sonnet", Label = "Claude 3 Sonnet", Tier = OpenRouterModelTier.Advanced },
                        new { SearchId = "openai/gpt-4-turbo", Label = "GPT-4 Turbo", Tier = OpenRouterModelTier.Advanced }
                    };

                    foreach (var item in strictList)
                    {
                        OpenRouterModelData? match = null;

                        // 1. Try Exact Match (ignoring case)
                        if (apiModels.TryGetValue(item.SearchId.ToLowerInvariant(), out var exact))
                        {
                            match = exact;
                        }
                        // 2. Try :free suffix (common in OpenRouter)
                        else if (apiModels.TryGetValue(item.SearchId.ToLowerInvariant() + ":free", out var freeVariant))
                        {
                            match = freeVariant;
                        }
                        // 3. Try StartsWith (Best Effort, but take the shortest/cleanest one to avoid 'latest')
                        else 
                        {
                            var candidates = apiModels.Values
                                .Where(m => m.Id.StartsWith(item.SearchId, StringComparison.OrdinalIgnoreCase))
                                .OrderBy(m => m.Id.Length) // Prefer shorter IDs (usually canonical)
                                .ToList();
                                
                            if (candidates.Any())
                            {
                                match = candidates.First();
                            }
                        }

                        if (match != null)
                        {
                            // Avoid adding the same model ID twice if it matches multiple search terms (unlikely with this list but safe)
                            if (list.Any(x => x.Id == match.Id)) continue;
                            
                            // Do not show "free" models in "Paid" tiers if the ID happens to match
                            // (e.g. mistral matches both free and paid variants sometimes)
                            // But usually :free suffix distinguishes them. 
                            
                            list.Add(new OpenRouterModelOption
                            {
                                Id = match.Id,
                                Name = match.Name,
                                DisplayName = item.Label,
                                ContextLength = match.ContextLength,
                                Tier = item.Tier
                            });
                        }
                    }
                }
                
                // Sort: Free -> Recommended -> FreeAdvanced -> Advanced
                return list
                    .OrderBy(x => x.Tier)
                    .ThenBy(x => x.DisplayName)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching OpenRouter models: {ex.Message}");
                return new System.Collections.Generic.List<OpenRouterModelOption>();
            }
        }
        
        private bool IsMatch(string id, string[] list)
        {
            return list.Any(item => id.Equals(item, StringComparison.OrdinalIgnoreCase) || id.StartsWith(item, StringComparison.OrdinalIgnoreCase));
        }

        private class OpenRouterResponse
        {
            public Choice[]? Choices { get; set; }
        }
        
        private class Choice
        {
            public Message? Message { get; set; }
        }
        
        private class Message
        {
            public string? Content { get; set; }
        }
        
        private class OpenRouterModelListResponse
        {
            public OpenRouterModelData[]? Data { get; set; }
        }
        
        private class OpenRouterModelData
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("context_length")]
            public int ContextLength { get; set; }
            public PricingInfo? Pricing { get; set; } 
        }
        
        private class PricingInfo 
        {
            public string? Prompt {get;set;}
            public string? Completion {get;set;}
        }
    }
    
    public enum OpenRouterModelTier
    {
        Free = 0,
        Recommended = 1,
        FreeAdvanced = 2,
        Advanced = 3
    }

    public class OpenRouterModelOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int ContextLength { get; set; }
        public OpenRouterModelTier Tier { get; set; }
        
        // Helper for UI grouping
        public string TierHeader => Tier switch
        {
            OpenRouterModelTier.Free => "🆓 Free Models (Recommended)",
            OpenRouterModelTier.FreeAdvanced => "🆓 Free Models (Advanced)",
            OpenRouterModelTier.Recommended => "⭐ Recommended (Credits Required)",
            OpenRouterModelTier.Advanced => "⚙ Advanced",
            _ => "Other"
        };
    }
}
