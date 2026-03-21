using System;

namespace EliteWhisper.Models
{
    public class LocalModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime DateInstalled { get; set; }
        public string Quantization { get; set; } = string.Empty;
        public string BaseParams { get; set; } = string.Empty; // e.g., "3B", "7B"
        public string Digest { get; set; } = string.Empty; // Optional, for Ollama compatibility
    }
}
