using System.Text.Json.Serialization;

namespace EliteWhisper.Models
{
    public class AIModelRegistryEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("tier")]
        public string Tier { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("dataDownloadUrl")]
        public string? DataDownloadUrl { get; set; }
        
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;
        
        [JsonPropertyName("dataFilename")]
        public string? DataFilename { get; set; }

        [JsonPropertyName("sizeMB")]
        public int SizeMB { get; set; }

        [JsonPropertyName("recommended")]
        public bool Recommended { get; set; }

        [JsonPropertyName("speedRating")]
        public int SpeedRating { get; set; } // 1-3

        [JsonPropertyName("accuracyRating")]
        public int AccuracyRating { get; set; } // 1-3

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        /// <summary>
        /// Engine type: "whisper", "sherpa", or null (legacy parakeet).
        /// Used by SpeechEngineSelector to determine which engine to use.
        /// </summary>
        [JsonPropertyName("engineType")]
        public string? EngineType { get; set; }
    }
}
