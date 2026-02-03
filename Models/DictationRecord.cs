using System;

namespace EliteWhisper.Models
{
    public class DictationRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Content { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public string ModelUsed { get; set; } = string.Empty;
        public string? ApplicationName { get; set; }
    }
}
