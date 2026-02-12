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
            
            // Default to 2.5 Flash if not specified or invalid
            string modelId = "gemini-2.5-flash"; 
            
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

            // Internal Helper to execute request
            async Task<string> ExecuteRequestAsync(string targetModel, CancellationToken ct)
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = prompt } }
                        }
                    }
                };
                
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{targetModel}:generateContent?key={apiKey}";
                var response = await _httpClient.PostAsync(url, content, ct);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[GeminiProvider] Error {response.StatusCode} for {targetModel}: {errorBody}");
                    
                    // Propagate 429/Timeout to caller for fallback handling
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                        response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                        (int)response.StatusCode == 429)
                    {
                        throw new LlmRateLimitException(errorBody);
                    }
                    
                    throw new HttpRequestException($"Gemini API Error {response.StatusCode}: {errorBody}");
                }
                
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson);
                return result?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? string.Empty;
            }

            try
            {
                return await ExecuteRequestAsync(modelId, cancellationToken);
            }
            catch (LlmRateLimitException)
            {
                // Internal Fallback: Try Gemini 2.0 Flash (often has separate quota or is lighter)
                if (modelId != "gemini-2.0-flash")
                {
                    System.Diagnostics.Debug.WriteLine("[GeminiProvider] Rate limit hit. Fallback to gemini-2.0-flash.");
                    try 
                    {
                        return await ExecuteRequestAsync("gemini-2.0-flash", cancellationToken);
                    }
                    catch
                    {
                        // If fallback fails, rethrow original or return empty (Fail-safe is handled by PostProcessingService)
                        throw; 
                    }
                }
                throw;
            }
        }

        public async Task<(System.Collections.Generic.List<GeminiModelInfo> Models, string DebugLog)> GetAvailableModelsAsync()
        {
            var sb = new StringBuilder();
            var encryptedKey = _configService.CurrentConfiguration.GeminiApiKey;
            var apiKey = _configService.DecryptApiKey(encryptedKey);
            
            if (string.IsNullOrEmpty(apiKey)) return (new System.Collections.Generic.List<GeminiModelInfo>(), "No API Key.");
            
            apiKey = apiKey.Trim();

            try
            {
                sb.AppendLine("Fetching models...");
                var response = await _httpClient.GetAsync($"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    sb.AppendLine($"API Error {response.StatusCode}: {errorBody}");
                    return (new System.Collections.Generic.List<GeminiModelInfo>(), sb.ToString());
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<GeminiModelListResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var finalModels = new System.Collections.Generic.List<GeminiModelInfo>();
                
                if (result?.Models != null)
                {
                    var apiModels = result.Models.ToDictionary(m => m.Name.Replace("models/", "").ToLowerInvariant());
                    
                    // User Requested Strict List
                    // 1. gemini-2.5-flash
                    // 2. gemini-2.0-flash-001
                    // 3. gemini-2.5-pro
                    
                    void TryAdd(string targetId, string displayName, string capability)
                    {
                        // Try exact match first
                        if (apiModels.TryGetValue(targetId, out var m))
                        {
                            finalModels.Add(new GeminiModelInfo
                            {
                                Id = targetId,
                                DisplayName = $"{displayName} ({capability})",
                                Description = m.Description ?? "",
                                InputTokenLimit = m.InputTokenLimit,
                                OutputTokenLimit = m.OutputTokenLimit
                            });
                            return;
                        }
                        
                        // Try finding a valid substitute if exact missing?
                        // E.g. if gemini-2.5-flash is missing, maybe gemini-2.5-flash-001?
                        // For now, let's stick to the user's requested IDs or closes matches if they are aliases.
                        // Actually, duplicate issue mentioned by user was likely alias + strict version.
                        // Let's look for "best match" for the INTENT.
                        
                        var bestMatch = apiModels.Keys.FirstOrDefault(k => k.StartsWith(targetId) && !k.Contains("latest"));
                        if (bestMatch != null && apiModels.TryGetValue(bestMatch, out var m2))
                        {
                             // Only add if not already in safe list (prevent duplicates if targetId is prefix of another target)
                             if (finalModels.Any(x => x.Id == bestMatch)) return;

                             finalModels.Add(new GeminiModelInfo
                             {
                                Id = bestMatch,
                                DisplayName = $"{displayName} ({capability})",
                                Description = m2.Description ?? "",
                                InputTokenLimit = m2.InputTokenLimit,
                                OutputTokenLimit = m2.OutputTokenLimit
                             });
                        }
                    }

                    // Strict Order & Logic
                    
                    // 1. Gemini 2.5 Flash (Fast · Recommended)
                    TryAdd("gemini-2.5-flash", "Gemini 2.5 Flash", "Fast · Recommended");
                    
                    // 2. Gemini 2.0 Flash (Stable) - User requested -001, but allow alias fallback
                    if (!finalModels.Any(x => x.DisplayName.Contains("2.0 Flash"))) // logical check
                    {
                        TryAdd("gemini-2.0-flash-001", "Gemini 2.0 Flash", "Stable");
                        // If strict version failed, try generic alias
                        if (!finalModels.Any(x => x.DisplayName.Contains("2.0 Flash")))
                        {
                             TryAdd("gemini-2.0-flash", "Gemini 2.0 Flash", "Stable");
                        }
                    }

                    // 3. Gemini 2.5 Pro (High Quality)
                    TryAdd("gemini-2.5-pro", "Gemini 2.5 Pro", "High Quality");
                    
                    // Fallback: If list is empty (API changes?), fall back to old logic but simpler
                    if (finalModels.Count == 0)
                    {
                         sb.AppendLine("Strict mapping failed. Falling back to discovery.");
                         // ... (Original logic could go here, but let's return empty/warning log to respect strictness)
                    }
                }
                
                sb.AppendLine($"Filtered down to {finalModels.Count} models.");
                return (finalModels, sb.ToString());
            }
            catch (Exception ex)
            {
                return (new System.Collections.Generic.List<GeminiModelInfo>(), $"Error: {ex.Message}");
            }
        }

        private double GetVersion(string id)
        {
             var match = System.Text.RegularExpressions.Regex.Match(id, @"gemini-(\d+(\.\d+)?)");
             if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v))
             {
                 return v;
             }
             return 0;
        }

        // FormatDisplayName and GetCapabilityTag are no longer used but kept if needed for fallback
        // Removed to keep code clean since we are hardcoding names now.
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
