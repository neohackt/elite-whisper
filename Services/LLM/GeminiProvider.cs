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
        private readonly string? _apiKey;
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";
        
        public string Name => "Google Gemini";
        
        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        
        public GeminiProvider(HttpClient httpClient, string? apiKey)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
        }
        
        public async Task<string> GenerateAsync(
            string prompt,
            LlmOptions options,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("Gemini API key is not configured.");
            
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = options.Temperature,
                    maxOutputTokens = options.MaxTokens
                }
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(
                $"{BaseUrl}?key={_apiKey}",
                content,
                cancellationToken);
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<GeminiResponse>(responseJson);
            
            return result?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? string.Empty;
        }
        
        private class GeminiResponse
        {
            public Candidate[]? Candidates { get; set; }
        }
        
        private class Candidate
        {
            public Content? Content { get; set; }
        }
        
        private class Content
        {
            public Part[]? Parts { get; set; }
        }
        
        private class Part
        {
            public string? Text { get; set; }
        }
    }
}
