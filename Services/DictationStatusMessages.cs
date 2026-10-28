using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    /// <summary>
    /// Maps a post-processing outcome to the status text shown in the widget.
    /// The widget must never claim that enhancement succeeded when it did not.
    /// </summary>
    public static class DictationStatusMessages
    {
        /// <summary>
        /// The status to show once the text has been inserted.
        /// </summary>
        /// <param name="result">The outcome of the enhancement attempt.</param>
        public static string ForPostProcessing(PostProcessingResult result) => result.Outcome switch
        {
            PostProcessingOutcome.Succeeded => "Done!",
            PostProcessingOutcome.Disabled => "Done!",
            PostProcessingOutcome.TimedOut => "Transcribed; enhancement timed out",
            PostProcessingOutcome.RateLimited => "Transcribed; rate limited",
            PostProcessingOutcome.NoProvider => "Transcribed; no AI provider",
            _ => "Transcribed; enhancement failed"
        };

        /// <summary>
        /// Whether the outcome counts as a successful enhancement. A disabled
        /// enhancement is not a success, but it is not a failure either: the
        /// transcription itself succeeded.
        /// </summary>
        public static bool IsEnhancementSuccessful(PostProcessingResult result) =>
            result.Outcome is PostProcessingOutcome.Succeeded or PostProcessingOutcome.Disabled;
    }
}
