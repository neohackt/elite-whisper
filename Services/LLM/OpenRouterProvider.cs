using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        private const string DefaultModel = "google/gemini-1.5-flash"; // Corrected ID
        
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
            
            // Defensive Check: OpenRouter models must contain "/" (e.g. "google/gemini-pro")
            // If user switches from Gemini (native) to OpenRouter without changing model, it might be "gemini-1.5-flash" (invalid)
            if (!modelId.Contains("/"))
            {
                 throw new InvalidOperationException(
                    $"Invalid OpenRouter Model ID: '{modelId}'. OpenRouter models must include the vendor prefix (e.g. 'google/gemini-1.5-flash'). Please select a valid model in the Modes tab.");
            }

            var requestBody = new
            {
                model = modelId,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                temperature = options.Temperature,
                max_tokens = options.MaxTokens
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("HTTP-Referer", "elite-whisper"); // Required by OpenRouter
            request.Headers.Add("X-Title", "Elite Whisper"); // Recommended
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                 var errorBody = await response.Content.ReadAsStringAsync();
                 System.Diagnostics.Debug.WriteLine($"[OpenRouter] Error {response.StatusCode}: {errorBody}");
                 
                 // Handle Rate Limits (429)
                 if ((int)response.StatusCode == 429)
                 {
                     throw new LlmRateLimitException($"OpenRouter Rate Limit: {errorBody}");
                 }

                 // Throwing here allows PostProcessingService to catch it and default to raw text (fail-safe)
                 // But we want to know why it failed.
                 throw new HttpRequestException($"OpenRouter API Error {response.StatusCode}: {errorBody}");
            }
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OpenRouterResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return result?.Choices?[0]?.Message?.Content ?? string.Empty;
        }

        public async Task<System.Collections.Generic.List<OpenRouterModelInfo>> GetAvailableModelsAsync()
        {
            var encryptedKey = _configService.CurrentConfiguration.OpenRouterApiKey;
            if (string.IsNullOrEmpty(encryptedKey)) return new System.Collections.Generic.List<OpenRouterModelInfo>();

            var apiKey = _configService.DecryptApiKey(encryptedKey);
            if (string.IsNullOrEmpty(apiKey)) return new System.Collections.Generic.List<OpenRouterModelInfo>();

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

                var list = new System.Collections.Generic.List<OpenRouterModelInfo>();
                if (result?.Data != null)
                {
                    foreach (var m in result.Data)
                    {
                        list.Add(new OpenRouterModelInfo
                        {
                            Id = m.Id,
                            Name = m.Name,
                            ContextLength = m.ContextLength
                        });
                    }
                }
                return list;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching OpenRouter models: {ex.Message}");
                return new System.Collections.Generic.List<OpenRouterModelInfo>();
            }
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
        }
    }
    
    // Public DTO for ViewModel
    public class OpenRouterModelInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int ContextLength { get; set; }
        
        // Formatted display
        public string DisplayName => $"{Name} ({Id})";
    }
}
