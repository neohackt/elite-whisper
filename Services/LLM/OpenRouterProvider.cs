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
        private readonly string? _apiKey;
        private const string BaseUrl = "https://openrouter.ai/api/v1/chat/completions";
        private const string DefaultModel = "google/gemini-flash-1.5";
        
        public string Name => "OpenRouter";
        
        public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
        
        public OpenRouterProvider(HttpClient httpClient, string? apiKey)
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
                throw new InvalidOperationException("OpenRouter API key is not configured.");
            
            var requestBody = new
            {
                model = DefaultModel,
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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OpenRouterResponse>(responseJson);
            
            return result?.Choices?[0]?.Message?.Content ?? string.Empty;
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
    }
}
