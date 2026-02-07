using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services.LLM
{
    /// <summary>
    /// LLM provider for Google Gemini API (free tier).
    /// Uses Gemini 1.5 Flash model.
    /// </summary>
    public class GeminiProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly WhisperConfigurationService _configService;
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent";
        
        public string Name => "Google Gemini";
        
        public bool IsAvailable => !string.IsNullOrEmpty(_configService.CurrentConfiguration.GeminiApiKey);
        
        public GeminiProvider(HttpClient httpClient, WhisperConfigurationService configService)
        {
            _httpClient = httpClient;
            _configService = configService;
        }
        
        public async Task<string> GenerateAsync(
            string prompt,
            LlmOptions options,
            CancellationToken cancellationToken)
        {
            string? encryptedKey = _configService.CurrentConfiguration.GeminiApiKey;
            var apiKey = _configService.DecryptApiKey(encryptedKey)?.Trim();
            
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Gemini API key is not configured or could not be decrypted.");
            
            // 7. Dynamic Model Selection
            // Use requested model or fallback to first available cached model, or last resort hardware fallback.
            string modelId = "gemini-1.5-flash"; // Ultimate fallback
            
            if (!string.IsNullOrEmpty(options.ModelId))
            {
                modelId = options.ModelId;
            }
            else
            {
                // Fallback: models are cached in config
                var firstCached = _configService.CurrentConfiguration.GeminiModels.FirstOrDefault();
                if (firstCached != null) modelId = firstCached.Id;
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            
            // Construct URL dynamically
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                 // 8. Error Handling
                 System.Diagnostics.Debug.WriteLine($"[GeminiProvider] Error {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                 
                 // Simple fallback if 404 or 400 and not using default
                 if ((response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.BadRequest) 
                     && modelId != "gemini-1.5-flash")
                 {
                     System.Diagnostics.Debug.WriteLine("[GeminiProvider] Falling back to gemini-1.5-flash");
                     url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
                     response = await _httpClient.PostAsync(url, content, cancellationToken);
                 }
                 
                 response.EnsureSuccessStatusCode();
            }
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson);
            
            return result?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? string.Empty;
        }

        public async Task<(System.Collections.Generic.List<GeminiModelInfo> Models, string DebugLog)> GetAvailableModelsAsync()
        {
            var sb = new StringBuilder();
            // Decrypt the key!
            var encryptedKey = _configService.CurrentConfiguration.GeminiApiKey;
            var apiKey = _configService.DecryptApiKey(encryptedKey);
            
            if (string.IsNullOrEmpty(apiKey)) return (new System.Collections.Generic.List<GeminiModelInfo>(), "No API Key (or decryption failed)");
            
            // Trim just in case
            apiKey = apiKey.Trim();

            try
            {
                // 1. Fetch models
                sb.AppendLine("Sending request...");
                var response = await _httpClient.GetAsync($"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    sb.AppendLine($"API Error {response.StatusCode}: {errorBody}");
                    System.Diagnostics.Debug.WriteLine($"[GeminiProvider] API Error: {errorBody}");
                    return (new System.Collections.Generic.List<GeminiModelInfo>(), sb.ToString());
                }

                var json = await response.Content.ReadAsStringAsync();
                sb.AppendLine($"JSON Received ({json.Length} chars). Start: {json.Substring(0, Math.Min(json.Length, 100))}");
                
                // Debug logging
                System.Diagnostics.Debug.WriteLine($"[GeminiProvider] Fetched models JSON (len={json.Length}): {json}");

                // Use snake_case naming policy if available or attributes. Attributes are safer.
                var result = JsonSerializer.Deserialize<GeminiModelListResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var list = new System.Collections.Generic.List<GeminiModelInfo>();
                if (result?.Models != null)
                {
                    sb.AppendLine($"Deserialized {result.Models.Length} models.");
                    foreach (var m in result.Models)
                    {
                        // 2. Filtering Rules
                        // Must support generateContent
                        bool supportsGen = m.SupportedGenerationMethods != null && m.SupportedGenerationMethods.Contains("generateContent");
                        sb.AppendLine($"Model: {m.Name}, GenContent: {supportsGen}");
                        
                        // Strict filtering as requested by user
                        if (!supportsGen) continue;
                        
                        // 3. Normalize IDs
                        string id = m.Name;
                        if (id.StartsWith("models/")) id = id.Substring(7);

                        list.Add(new GeminiModelInfo
                        {
                            Id = id,
                            DisplayName = m.DisplayName ?? id,
                            Description = m.Description ?? "",
                            InputTokenLimit = m.InputTokenLimit,
                            OutputTokenLimit = m.OutputTokenLimit
                        });
                    }
                }
                else
                {
                    sb.AppendLine("Deserialization result or Models array was null.");
                }
                
                return (list, sb.ToString());
            }
            catch (Exception ex)
            {
                var error = $"Error fetching Gemini models: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(error);
                sb.AppendLine(error);
                return (new System.Collections.Generic.List<GeminiModelInfo>(), sb.ToString());
            }
        }
    }

    // Public DTOs for API binding
    public class GeminiResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("candidates")]
        public Candidate[]? Candidates { get; set; }
    }
    
    public class Candidate
    {
        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public Content? Content { get; set; }
    }
    
    public class Content
    {
        [System.Text.Json.Serialization.JsonPropertyName("parts")]
        public Part[]? Parts { get; set; }
    }
    
    public class Part
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class GeminiModelListResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("models")]
        public GeminiApiModel[]? Models { get; set; }
    }

    public class GeminiApiModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("inputTokenLimit")]
        public int InputTokenLimit { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("outputTokenLimit")]
        public int OutputTokenLimit { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("supportedGenerationMethods")]
        public string[]? SupportedGenerationMethods { get; set; }
    }
}
