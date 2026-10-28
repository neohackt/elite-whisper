namespace EliteWhisper.Models
{
    /// <summary>
    /// Why a post-processing (AI enhancement) attempt ended.
    /// </summary>
    /// <remarks>
    /// Cancellation is deliberately absent: a cancelled enhancement is
    /// propagated to the caller as an <see cref="System.OperationCanceledException"/>
    /// rather than returned as a result, so a cancelled dictation is abandoned
    /// instead of inserting the raw transcript.
    /// </remarks>
    public enum PostProcessingOutcome
    {
        /// <summary>Enhancement was not requested for this dictation mode.</summary>
        Disabled,

        /// <summary>A provider produced the enhanced text.</summary>
        Succeeded,

        /// <summary>Enhancement was requested but no provider was available.</summary>
        NoProvider,

        /// <summary>A provider failed, or answered with an empty response.</summary>
        ProviderFailed,

        /// <summary>A provider did not answer in time.</summary>
        TimedOut,

        /// <summary>A provider was rate limited and the fallback chain was exhausted.</summary>
        RateLimited
    }

    /// <summary>
    /// The result of a post-processing attempt: the text to use plus an
    /// explicit statement of what happened.
    /// </summary>
    public sealed class PostProcessingResult
    {
        /// <summary>
        /// The text to insert. This is the enhanced text when
        /// <see cref="Enhanced"/> is true, and the original transcript otherwise.
        /// </summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>True when <see cref="Text"/> was produced by an LLM.</summary>
        public bool Enhanced { get; init; }

        /// <summary>
        /// Name of the provider that actually produced <see cref="Text"/>,
        /// including a provider that succeeded as a fallback. Null when no
        /// provider succeeded.
        /// </summary>
        public string? ProviderName { get; init; }

        /// <summary>What happened during the attempt.</summary>
        public PostProcessingOutcome Outcome { get; init; }

        /// <summary>
        /// Short, non-sensitive description of a failure (an exception type
        /// name). Null when the attempt succeeded.
        /// </summary>
        public string? FailureReason { get; init; }
    }
}
