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
    /// LLM provider for local Ollama instance.
    /// Endpoint: http://localhost:11434/api/generate
    /// Default model: phi3:mini
    /// </summary>
    public class OllamaProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "http://localhost:11434";
        private const string DefaultModel = "phi3:mini";
        
        public string Name => "Local (Ollama)";
        
        public bool IsAvailable
        {
            get
            {
                try
                {
                    // Quick ping to check if Ollama is running
                    var response = _httpClient.GetAsync($"{BaseUrl}/api/tags", new CancellationTokenSource(1000).Token).Result;
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }
        
        public OllamaProvider(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        
        public async Task<string> GenerateAsync(
            string prompt,
            LlmOptions options,
            CancellationToken cancellationToken)
        {
            // Use requested model or default
            var modelId = !string.IsNullOrEmpty(options.ModelId) ? options.ModelId : DefaultModel;

            var requestBody = new
            {
                model = modelId,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = options.Temperature,
                    num_predict = options.MaxTokens
                }
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(
                $"{BaseUrl}/api/generate",
                content,
                cancellationToken);
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson);
            
            return result?.Response ?? string.Empty;
        }

        public async Task<System.Collections.Generic.List<LocalModelInfo>> GetInstalledModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/tags");
                if (!response.IsSuccessStatusCode) return new System.Collections.Generic.List<LocalModelInfo>();

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaTagsResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var list = new System.Collections.Generic.List<LocalModelInfo>();
                if (result?.Models != null)
                {
                    foreach (var m in result.Models)
                    {
                        list.Add(new LocalModelInfo
                        {
                            Name = m.Name,
                            Size = FormatSize(m.Size),
                            Digest = m.Digest
                        });
                    }
                }
                return list;
            }
            catch
            {
                return new System.Collections.Generic.List<LocalModelInfo>();
            }
        }

        public async Task PullModelAsync(string modelName, IProgress<int> progress)
        {
             var requestBody = new { name = modelName, stream = true };
             var json = JsonSerializer.Serialize(requestBody);
             var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/pull")
             {
                 Content = new StringContent(json, Encoding.UTF8, "application/json")
             };

             // This should be streamed ideally, but for MVP we might just wait or do simple progress if possible via ReadAsStream
             using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
             response.EnsureSuccessStatusCode();

             using var stream = await response.Content.ReadAsStreamAsync();
             using var reader = new System.IO.StreamReader(stream);

             while (!reader.EndOfStream)
             {
                 var line = await reader.ReadLineAsync();
                 if (string.IsNullOrEmpty(line)) continue;
                 
                 try 
                 {
                    // Parse line for progress
                    var status = JsonSerializer.Deserialize<OllamaStatus>(line);
                    if (status != null && status.Total > 0 && status.Completed > 0)
                    {
                        var percentage = (int)((double)status.Completed / status.Total * 100);
                        progress?.Report(percentage);
                    }
                 }
                 catch { /* ignore parse errors */ }
             }
             progress?.Report(100);
        }

        public async Task DeleteModelAsync(string modelName)
        {
             var requestBody = new { name = modelName };
             var json = JsonSerializer.Serialize(requestBody);
             var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/api/delete")
             {
                 Content = new StringContent(json, Encoding.UTF8, "application/json")
             };
             
             var response = await _httpClient.SendAsync(request);
             response.EnsureSuccessStatusCode();
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        
        private class OllamaResponse
        {
            public string? Response { get; set; }
        }

        private class OllamaTagsResponse
        {
            public OllamaModel[]? Models { get; set; }
        }

        private class OllamaModel
        {
            public string Name { get; set; } = "";
            public long Size { get; set; }
            public string Digest { get; set; } = "";
        }

        private class OllamaStatus
        {
            public string Status { get; set; } = "";
            public long Total { get; set; }
            public long Completed { get; set; }
        }
    }
}
