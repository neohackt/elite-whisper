using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EliteWhisper.Models;
using EliteWhisper.Services.LLM;

namespace EliteWhisper.Services
{
    /// <summary>
    /// Handles post-processing of transcriptions using LLMs.
    ///
    /// The outcome is always explicit: the returned
    /// <see cref="PostProcessingResult"/> states whether the text was enhanced,
    /// which provider produced it, and why an enhancement did not happen. A
    /// failed enhancement still yields the original transcript, but never as an
    /// indistinguishable success.
    ///
    /// Cancellation is the one case that is not returned as a result: it is
    /// rethrown as <see cref="OperationCanceledException"/> so the caller
    /// abandons the dictation instead of inserting the raw transcript.
    /// </summary>
    public class PostProcessingService
    {
        private readonly LlmService _llmService;

        /// <summary>Redacts credential-shaped text before it reaches a log.</summary>
        private static readonly Regex SecretPattern = new(
            @"(?i)\b(key|api[_-]?key|access[_-]?token|authorization|bearer)\b\s*[=:]\s*""?[^\s""&,;]+",
            RegexOptions.Compiled);

        public PostProcessingService(LlmService llmService)
        {
            _llmService = llmService;
        }

        /// <summary>
        /// Process raw transcription text with LLM if enabled.
        /// </summary>
        /// <param name="rawText">Raw transcription from ASR engine.</param>
        /// <param name="mode">Active dictation mode.</param>
        /// <param name="cancellationToken">Cancels the enhancement request.</param>
        /// <returns>
        /// The text to use and an explicit outcome. The text is the enhanced
        /// text on success and the original transcript otherwise.
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// The enhancement was cancelled. Never swallowed, never turned into
        /// the raw transcript.
        /// </exception>
        public async Task<PostProcessingResult> ProcessAsync(string rawText, DictationMode mode, CancellationToken cancellationToken = default)
        {
            // Skip if post-processing disabled
            if (!mode.EnablePostProcessing || mode.PostProcess == null)
            {
                DebugHelper.Log($"[PostProcessing] Disabled for mode {mode.Id} (EnablePostProcessing: {mode.EnablePostProcessing})");
                return new PostProcessingResult
                {
                    Text = rawText,
                    Enhanced = false,
                    Outcome = PostProcessingOutcome.Disabled
                };
            }

            // Select provider
            var provider = _llmService.SelectProvider(mode);
            if (provider == null)
            {
                DebugHelper.Log("[PostProcessing] No provider available; using the raw transcript.");
                return Failure(rawText, PostProcessingOutcome.NoProvider, "no provider available");
            }

            Debug.WriteLine($"[PostProcessing] Using provider: {provider.Name}");
            DebugHelper.Log($"[PostProcessing] Selected Provider: {provider.Name}");

            // Build prompt from template. The prompt and the response are
            // private content and are never written to a log.
            var prompt = mode.PostProcess.PromptTemplate.Replace("{{text}}", rawText);
            var options = BuildOptions(mode, provider);

            try
            {
                // Never call a provider once the operation was cancelled.
                cancellationToken.ThrowIfCancellationRequested();

                DebugHelper.Log("[PostProcessing] Calling GenerateAsync...");

                var enhancedText = await provider.GenerateAsync(prompt, options, cancellationToken);

                // Sanitization (Centralized)
                enhancedText = ModelOutputSanitizer.Sanitize(enhancedText);

                // Email-mode-specific cleanup: remove explanations, intros, markdown fences
                if (mode.Id == "email")
                {
                    enhancedText = CleanEmailOutput(enhancedText);
                }

                if (string.IsNullOrWhiteSpace(enhancedText))
                {
                    // An empty reply is a failed enhancement, not a success that
                    // happens to produce no text: inserting it would lose the
                    // transcript entirely.
                    DebugHelper.Log($"[PostProcessing] Provider {provider.Name} returned an empty response; using the raw transcript.");
                    return Failure(rawText, PostProcessingOutcome.ProviderFailed, "empty response");
                }

                DebugHelper.Log($"[PostProcessing] Success. Original length: {rawText.Length}, Enhanced length: {enhancedText.Length}");
                return new PostProcessingResult
                {
                    Text = enhancedText,
                    Enhanced = true,
                    ProviderName = provider.Name,
                    Outcome = PostProcessingOutcome.Succeeded
                };
            }
            catch (LlmRateLimitException)
            {
                DebugHelper.Log($"[PostProcessing] RATE LIMITED (Provider: {provider.Name}). Attempting fallback...");
                return await RunFallbacksAsync(rawText, mode, provider, prompt, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                // Cancellation is never a timeout and never a provider
                // failure, whether or not the caller's token is flagged: a
                // timed-out request is reported as a timeout below.
                var kind = ClassifyCancellation(ex, cancellationToken);
                if (kind == CancellationKind.TimedOut)
                {
                    DebugHelper.Log($"[PostProcessing] TIMEOUT (Provider: {provider.Name}). Using the raw transcript.");
                    return Failure(rawText, PostProcessingOutcome.TimedOut, Describe(ex));
                }

                DebugHelper.Log($"[PostProcessing] CANCELLED (Provider: {provider.Name}).");
                throw;
            }
            catch (TimeoutException ex)
            {
                // The provider's own deadline, for example llama.cpp.
                DebugHelper.Log($"[PostProcessing] TIMEOUT (Provider: {provider.Name}). Using the raw transcript.");
                return Failure(rawText, PostProcessingOutcome.TimedOut, Describe(ex));
            }
            catch (Exception ex)
            {
                var reason = Describe(ex);
                DebugHelper.Log($"[PostProcessing] FAILED (Provider: {provider.Name}): {reason}");
                Debug.WriteLine($"[PostProcessing] {ex}");
                return Failure(rawText, PostProcessingOutcome.ProviderFailed, reason);
            }
        }

        /// <summary>
        /// The documented fallback chain, entered only when the selected
        /// provider was rate limited. The order (Gemini, then Ollama) and the
        /// fallback options are unchanged.
        /// </summary>
        private async Task<PostProcessingResult> RunFallbacksAsync(
            string rawText,
            DictationMode mode,
            ILlmProvider primary,
            string prompt,
            CancellationToken cancellationToken)
        {
            var candidates = new List<(ILlmProvider Provider, LlmOptions Options)>();

            var gemini = _llmService.GeminiProvider;
            if (gemini != null && !ReferenceEquals(gemini, primary) && gemini.IsAvailable)
            {
                candidates.Add((gemini, FallbackOptions()));
            }

            var ollama = _llmService.OllamaProvider;
            if (ollama != null && !ReferenceEquals(ollama, primary) && ollama.IsAvailable)
            {
                candidates.Add((ollama, FallbackOptions()));
            }

            Exception? lastFailure = null;

            foreach (var (provider, options) in candidates)
            {
                // Never start another provider once the operation was
                // cancelled: cancellation ends the dictation.
                if (cancellationToken.IsCancellationRequested)
                {
                    DebugHelper.Log($"[PostProcessing] CANCELLED before fallback ({provider.Name}).");
                    throw new OperationCanceledException(cancellationToken);
                }

                try
                {
                    DebugHelper.Log($"[PostProcessing] Fallback to {provider.Name}...");
                    var fallbackText = await provider.GenerateAsync(prompt, options, cancellationToken);
                    fallbackText = ModelOutputSanitizer.Sanitize(fallbackText);

                    // Email-mode-specific cleanup for fallback too
                    if (mode.Id == "email")
                    {
                        fallbackText = CleanEmailOutput(fallbackText);
                    }

                    if (string.IsNullOrWhiteSpace(fallbackText))
                    {
                        // An empty fallback answer is a failed attempt, not a
                        // success: keep the transcript and try the next one.
                        DebugHelper.Log($"[PostProcessing] Fallback {provider.Name} returned an empty response.");
                        lastFailure = new InvalidOperationException($"{provider.Name} returned an empty response");
                        continue;
                    }

                    DebugHelper.Log($"[PostProcessing] Fallback succeeded via {provider.Name}.");
                    return new PostProcessingResult
                    {
                        Text = fallbackText,
                        Enhanced = true,
                        ProviderName = provider.Name,
                        Outcome = PostProcessingOutcome.Succeeded
                    };
                }
                catch (OperationCanceledException)
                {
                    // A cancelled fallback is a cancellation, never a reason to
                    // try yet another provider and never a raw-text fallback.
                    DebugHelper.Log($"[PostProcessing] CANCELLED during fallback ({provider.Name}).");
                    throw;
                }
                catch (Exception ex)
                {
                    DebugHelper.Log($"[PostProcessing] Fallback {provider.Name} failed: {Describe(ex)}");
                    lastFailure = ex;
                }
            }

            DebugHelper.Log("[PostProcessing] All fallbacks failed. Using the raw transcript.");
            return Failure(
                rawText,
                PostProcessingOutcome.RateLimited,
                lastFailure == null ? "rate limited" : Describe(lastFailure));
        }

/// <summary>
        /// Email-mode-specific output cleanup: removes explanatory sections, introductory phrases,
        /// and enclosing markdown fences. Only applies to Email mode.
        /// </summary>
        private static string CleanEmailOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // 1. Remove enclosing markdown fences (```email, ```text, ```markdown, ```) that wrap the entire output
            text = Regex.Replace(
                text,
                @"^```(?:email|text|markdown|md)?\s*\n([\s\S]*?)\n```\s*$",
                "$1",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // 2. Remove standalone introductory phrases that occur BEFORE the email content
            // These are typically at the start of the output
            var introPatterns = new[]
            {
                @"^here is the (?:revised|improved|updated|edited|new) email[.:]\s*",
                @"^here's the (?:revised|improved|updated|edited|new) email[.:]\s*",
                @"^below is the (?:revised|improved|updated|edited|new) email[.:]\s*",
                @"^here is the email[.:]\s*",
                @"^here's the email[.:]\s*",
                @"^below is the email[.:]\s*",
            };

            foreach (var pattern in introPatterns)
            {
                text = Regex.Replace(text, pattern, string.Empty, RegexOptions.IgnoreCase);
            }

            // 3. Remove the first recognized standalone explanation heading and everything after it.
            // The heading must occupy its own line and match exactly (case-insensitive),
            // with optional paired Markdown bold markers and an optional trailing colon.
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int cutIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsExplanationHeading(lines[i]))
                {
                    cutIndex = i;
                    break;
                }
            }
            if (cutIndex >= 0)
            {
                text = string.Join("\n", lines, 0, cutIndex);
            }

            // 4. Trim any trailing whitespace
            return text.Trim();
        }

        /// <summary>
        /// Returns true when <paramref name="line"/> is a standalone recognized
        /// explanation heading (optionally wrapped in paired **bold** markers
        /// and/or followed by a colon). Used only by Email mode cleanup.
        /// </summary>
        private static bool IsExplanationHeading(string line)
        {
            var s = (line ?? string.Empty).Trim();
            if (s.Length == 0)
                return false;

            // Strip a trailing colon (handles "Explanation:" and "**Explanation**:")
            if (s.EndsWith(":"))
                s = s.Substring(0, s.Length - 1).Trim();

            // Strip a correctly paired bold marker (handles "**Explanation:**")
            if (s.StartsWith("**") && s.EndsWith("**") && s.Length >= 4)
                s = s.Substring(2, s.Length - 4).Trim();

            // Strip a trailing colon that was inside the bold markers
            if (s.EndsWith(":"))
                s = s.Substring(0, s.Length - 1).Trim();

            // Strip a paired bold marker again (handles "**Explanation**:")
            if (s.StartsWith("**") && s.EndsWith("**") && s.Length >= 4)
                s = s.Substring(2, s.Length - 4).Trim();

            if (s.EndsWith(":"))
                s = s.Substring(0, s.Length - 1).Trim();

            var normalized = Regex.Replace(s.ToLowerInvariant(), @"\s+", " ");

            var headings = new HashSet<string>(StringComparer.Ordinal)
            {
                "explanation",
                "explanation of changes",
                "explanation of the changes",
                "changes made",
                "changes",
                "why this works",
                "key changes",
                "summary of changes",
                "what i changed",
                "how this improves",
            };

            return headings.Contains(normalized);
        }

        /// <summary>
        /// The options used for the selected provider.
        /// </summary>
        private static LlmOptions BuildOptions(DictationMode mode, ILlmProvider provider)
        {
            var postProcess = mode.PostProcess!;

            // Resolve Model ID based on provider
            string? targetModelId = null;
            if (provider is OllamaProvider) targetModelId = postProcess.PreferredLocalModel;
            else if (provider is GeminiProvider) targetModelId = postProcess.PreferredGeminiModel;
            else if (provider is OpenRouterProvider) targetModelId = postProcess.PreferredOpenRouterModel;
            else if (provider is LocalLlmProvider) targetModelId = postProcess.PreferredBuiltInModel;

            // Fallback to legacy field if specific one is unset (migration support)
            if (string.IsNullOrEmpty(targetModelId)) targetModelId = postProcess.PreferredModel;

            return new LlmOptions
            {
                Temperature = 0.2f,
                MaxTokens = 4096, // Increased to allow reasoning models (DeepSeek-R1) to finish thinking
                ModelId = targetModelId
            };
        }

        /// <summary>
        /// The options used for a fallback provider. Unchanged.
        /// </summary>
        private static LlmOptions FallbackOptions() => new()
        {
            Temperature = 0.2f,
            MaxTokens = 512
        };

        private static PostProcessingResult Failure(string rawText, PostProcessingOutcome outcome, string? reason) => new()
        {
            Text = rawText,
            Enhanced = false,
            Outcome = outcome,
            FailureReason = reason
        };

        private enum CancellationKind
        {
            Cancelled,
            TimedOut
        }

        /// <summary>
        /// Decides whether a cancellation is a genuine provider timeout or a
        /// cancellation.
        /// </summary>
        /// <remarks>
        /// HttpClient reports its own timeout as a cancellation, so a cancelled
        /// task is only a timeout when it carries a timeout marker. The
        /// caller's token wins: if it is cancelled, the request was cancelled
        /// by the user. Everything else that cancels is a cancellation and is
        /// rethrown, even when the caller's token is not flagged.
        /// </remarks>
        private static CancellationKind ClassifyCancellation(Exception ex, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancellationKind.Cancelled;
            }

            return ContainsTimeout(ex)
                ? CancellationKind.TimedOut
                : CancellationKind.Cancelled;
        }

        /// <summary>
        /// Whether the exception chain carries a timeout marker.
        /// </summary>
        private static bool ContainsTimeout(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is TimeoutException)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A short, credential-free description of a failure.
        /// </summary>
        private static string Describe(Exception ex) =>
            $"{ex.GetType().Name}: {SecretPattern.Replace(ex.Message, "$1=<redacted>")}";
    }
}
