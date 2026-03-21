using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    public class LlamaCppService
    {
        private readonly WhisperConfigurationService _configService;
        private Process? _activeProcess;
        private readonly object _lock = new();

        public LlamaCppService(WhisperConfigurationService configService)
        {
            _configService = configService;
        }

        public async Task<string> GenerateAsync(string modelPath, string prompt, LlmOptions options, CancellationToken cancellationToken)
        {
            // 1. Locate executable
            // Assumes structure: %AppDir%/runtimes/win-x64/native/llama.exe
            // Or just next to main exe for simplicity in release.
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "win-x64", "native", "llama.exe");
            
            if (!File.Exists(exePath))
            {
                // Fallback: Check root
                exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "llama.exe");
                if (!File.Exists(exePath))
                {
                    throw new FileNotFoundException("llama.exe not found. Please ensure it is installed in runtimes/win-x64/native/ or the app root.");
                }
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

            var outputBuilder = new StringBuilder();
            var tcs = new TaskCompletionSource<string>();

            using (cancellationToken.Register(() => 
            {
                // Kill process on cancel
                KillActiveProcess();
                tcs.TrySetCanceled();
            }))
            {
                lock (_lock)
                {
                    if (_activeProcess != null && !_activeProcess.HasExited)
                    {
                        KillActiveProcess(); // Ensure only one runs at a time
                    }

                    _activeProcess = new Process { StartInfo = startInfo };
                    
                    _activeProcess.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    _activeProcess.ErrorDataReceived += (s, e) =>
                    {
                         // Using error stream for debug logging if needed
                         if (!string.IsNullOrWhiteSpace(e.Data))
                         {
                             System.Diagnostics.Debug.WriteLine($"[LlamaCpp] {e.Data}");
                         }
                    };
                    
                    try
                    {
                        _activeProcess.Start();
                        _activeProcess.BeginOutputReadLine();
                        _activeProcess.BeginErrorReadLine();
                    }
                    catch (Exception ex)
                    {
                         throw new InvalidOperationException($"Failed to start llama.exe: {ex.Message}", ex);
                    }
                }

                // Wait for exit
                await _activeProcess.WaitForExitAsync(cancellationToken);
                
                lock (_lock)
                {
                    _activeProcess = null;
                }
            }

            var rawOutput = outputBuilder.ToString();
            return StripThinking(rawOutput);
        }

        private void KillActiveProcess()
        {
            try
            {
                if (_activeProcess != null && !_activeProcess.HasExited)
                {
                    _activeProcess.Kill();
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
