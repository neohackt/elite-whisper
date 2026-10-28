using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    /// <summary>
    /// Thrown when a local llama.cpp inference is cancelled by the caller, for
    /// example when the user stops dictation while the model is still running.
    /// Distinct from <see cref="TimeoutException"/> so callers can tell a user
    /// cancellation apart from a model that ran too long.
    /// </summary>
    public sealed class LlamaInferenceCanceledException : OperationCanceledException
    {
        public LlamaInferenceCanceledException(string message) : base(message) { }
    }

    public class LlamaCppService
    {
        /// <summary>
        /// Names the llama.cpp executable may be published under.
        /// "llama.exe" is the name staged by download_llama.ps1; "llama-cli.exe"
        /// is the name upstream releases ship.
        /// </summary>
        private static readonly string[] ExecutableNames = { "llama.exe", "llama-cli.exe" };

        private readonly WhisperConfigurationService _configService;
        private Process? _activeProcess;
        private readonly object _lock = new();

        public LlamaCppService(WhisperConfigurationService configService)
        {
            _configService = configService;
        }

        /// <summary>
        /// True when a llama.cpp executable can actually be located. Used by the
        /// provider so a missing runtime is never reported as available.
        /// </summary>
        public bool IsRuntimeAvailable => ResolveExecutable() != null;

        /// <summary>
        /// Every location searched for the llama.cpp executable, in priority order.
        /// </summary>
        public static IReadOnlyList<string> GetExecutableCandidates()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>();

            // Published layout: runtimes\win-x64\native\llama.exe
            foreach (string name in ExecutableNames)
                candidates.Add(Path.Combine(baseDir, "runtimes", "win-x64", "native", name));

            // Installed layout: <app>\llama.exe
            foreach (string name in ExecutableNames)
                candidates.Add(Path.Combine(baseDir, name));

            // Development layout: <repo>\dist\llama.exe, staged by download_llama.ps1.
            // Resolved from the assembly location, never from the working directory.
            foreach (string repoRoot in GetRepositoryRoots(baseDir))
                foreach (string name in ExecutableNames)
                    candidates.Add(Path.Combine(repoRoot, "dist", name));

            return candidates;
        }

        /// <summary>
        /// Returns the llama.cpp executable path, or null when none is present.
        /// </summary>
        public static string? ResolveExecutable()
        {
            foreach (string candidate in GetExecutableCandidates())
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Walks up from the build output looking for the repository root.
        /// </summary>
        private static IEnumerable<string> GetRepositoryRoots(string startDirectory)
        {
            var dir = new DirectoryInfo(startDirectory);
            for (int depth = 0; depth < 8 && dir != null; depth++)
            {
                bool isRepoRoot = File.Exists(Path.Combine(dir.FullName, "EliteWhisper.csproj"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"));
                if (isRepoRoot) yield return dir.FullName;
                dir = dir.Parent;
            }
        }

        public async Task<string> GenerateAsync(string modelPath, string prompt, LlmOptions options, CancellationToken cancellationToken)
        {
            // 1. Locate executable
            string? exePath = ResolveExecutable();

            if (exePath == null)
            {
                throw new FileNotFoundException(
                    "llama.cpp runtime not found. Looked for " + string.Join(" / ", ExecutableNames) +
                    " in: " + string.Join("; ", GetExecutableCandidates()) +
                    ". Run download_llama.ps1 to provision it.");
            }

            var settings = _configService.CurrentConfiguration.LlamaSettings;

            // 2. Build Arguments
            // -m: Model path
            // -p: Prompt
            // -n: Num predict
            // -c: Context size
            // --temp: Temperature
            // -t: Threads
            // --no-display-prompt: Don't echo prompt
            // --log-disable: Less noise
            
            var argsBuilder = new StringBuilder();
            argsBuilder.Append($"-m \"{modelPath}\" ");
            argsBuilder.Append($"-p \"{EscapePrompt(prompt)}\" ");
            argsBuilder.Append($"-n {options.MaxTokens} ");
            argsBuilder.Append($"-c {settings.ContextSize} ");
            argsBuilder.Append($"--temp {options.Temperature} ");
            argsBuilder.Append($"-t {settings.Threads} ");
            argsBuilder.Append($"--top-p {settings.TopP} ");
            argsBuilder.Append($"--repeat-penalty {settings.RepeatPenalty} ");
            
            if (settings.GpuLayers > 0)
            {
                argsBuilder.Append($"-ngl {settings.GpuLayers} ");
            }

            argsBuilder.Append("--no-display-prompt ");
            argsBuilder.Append("--log-disable ");
            // Single turn, plain IO and no colour: the CLI otherwise starts an
            // interactive session and emits ANSI escapes into the captured text.
            argsBuilder.Append("-st ");
            argsBuilder.Append("--simple-io ");
            argsBuilder.Append("--color off ");

            // llama.cpp writes the generated conversation to this file; stdout also
            // carries the banner, the echoed prompt and timing statistics.
            string outputFile = Path.Combine(Path.GetTempPath(), $"llama_out_{Guid.NewGuid():N}.txt");
            argsBuilder.Append($"-o \"{outputFile}\" ");

            // 3. Process Start Info
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = argsBuilder.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            bool hasTimeout = settings.TimeoutSeconds > 0;
            using var timeoutCts = new CancellationTokenSource();
            if (hasTimeout)
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var process = new Process { StartInfo = startInfo };

            try
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                     // Using error stream for debug logging if needed
                     if (!string.IsNullOrWhiteSpace(e.Data))
                     {
                         errorBuilder.AppendLine(e.Data);
                         System.Diagnostics.Debug.WriteLine($"[LlamaCpp] {e.Data}");
                     }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                     throw new InvalidOperationException($"Failed to start llama.exe: {ex.Message}", ex);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Only one inference at a time: stop a previous one still running.
                lock (_lock)
                {
                    if (_activeProcess != null && !_activeProcess.HasExited)
                    {
                        TryKill(_activeProcess);
                    }

                    _activeProcess = process;
                }

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);

                    if (hasTimeout && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        throw new TimeoutException(
                            $"llama.cpp inference timed out after {settings.TimeoutSeconds}s. " +
                            "Increase LlamaSettings.TimeoutSeconds or use a smaller model.");

                    throw new LlamaInferenceCanceledException("llama.cpp inference was cancelled.");
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"llama.cpp exited with code {process.ExitCode}." +
                        (string.IsNullOrWhiteSpace(errorBuilder.ToString()) ? "" : " " + errorBuilder.ToString().Trim()));
                }

                string result = StripThinking(outputBuilder.ToString());

                // Prefer the file llama.cpp wrote: it holds only the generated
                // conversation, without the banner or the timing statistics.
                try
                {
                    if (File.Exists(outputFile))
                    {
                        string fileText = File.ReadAllText(outputFile);
                        string extracted = ExtractAssistantText(fileText);
                        if (!string.IsNullOrWhiteSpace(extracted))
                        {
                            result = StripThinking(extracted);
                        }
                    }
                }
                catch { /* Fall back to the captured stdout */ }

                return result;
            }
            finally
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_activeProcess, process))
                        _activeProcess = null;
                }

                process.Dispose();

                try { if (File.Exists(outputFile)) File.Delete(outputFile); } catch { }
            }
        }

        /// <summary>
        /// Extracts the assistant reply from the conversation llama.cpp writes:
        /// "User: ... \n\nAssistant: &lt;reply&gt;".
        /// </summary>
        private static string ExtractAssistantText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            const string marker = "Assistant:";
            int index = text.LastIndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return text.Trim();

            return text[(index + marker.Length)..].Trim();
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { /* Ignore */ }
        }

        private string EscapePrompt(string prompt)
        {
            // Simple escaping for command line
            return prompt.Replace("\"", "\\\"");
        }

        private string StripThinking(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Remove <think> blocks
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"<think>[\s\S]*?(?:</think>|$)",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ).Trim();

            return cleaned;
        }
    }
}
