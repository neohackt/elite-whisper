using System;

namespace EliteWhisper.Services.LLM
{
    public class LlmRateLimitException : Exception
    {
        public LlmRateLimitException(string message) : base(message) { }
    }
}
