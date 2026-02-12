using System;
using System.Text.RegularExpressions;

namespace EliteWhisper.Services
{
    public static class ModelOutputSanitizer
    {
        public static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // 1. Remove <think>...</think> blocks (DeepSeek R1 style)
            // Use Singleline mode (s) so . matches newlines
            text = Regex.Replace(
                text,
                @"<think>[\s\S]*?</think>",
                string.Empty,
                RegexOptions.IgnoreCase
            );

            // 2. Remove leading reasoning paragraphs (best-effort)
            // Common conversational fillers or self-corrections from some models
            var markers = new[]
            {
                "thinking:",
                "reasoning:",
                "analysis:",
                "okay, let's",
                "let's think",
                "here is the",
                "sure, here" 
            };

            var trimmed = text.TrimStart();
            foreach (var marker in markers)
            {
                if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    // If it starts with a marker, try to split by double newline to find the actual content
                    // e.g. "Okay, let's fix this.\n\nCorrected Text..."
                    var split = text.Split(new[] { "\n\n", "\r\n\r\n" }, 2, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (split.Length == 2)
                    {
                        // Assume the second part is the actual content
                        text = split[1];
                        break; 
                    }
                    else if (split.Length == 1 && text.Length > marker.Length + 50) 
                    {
                        // Fallback: If no double newline but text is long, maybe it's just one block?
                        // Dangerous to strip if we aren't sure. 
                        // The user's reference impl suggests splitting by \n\n.
                        // We will stick to the safe split for now.
                    }
                }
            }

            return text.Trim();
        }
    }
}
