using System;

namespace EliteWhisper.Models
{
    public class DictationRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Content { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public int WordCount { get; set; }
        public int DurationSeconds { get; set; }
        public string ModelUsed { get; set; } = string.Empty;
        public string? ApplicationName { get; set; }

        /// <summary>
        /// Whether AI enhancement succeeded. Null means "not applicable"
        /// (enhancement was disabled) or "not recorded" (the record was saved
        /// before this field existed). False means enhancement was requested
        /// and failed.
        /// </summary>
        public bool? EnhancementSucceeded { get; set; }

        /// <summary>
        /// Name of the LLM provider that produced the text, including a
        /// provider that succeeded as a fallback. Null when no provider
        /// succeeded.
        /// </summary>
        public string? EnhancementProvider { get; set; }
    }
}
