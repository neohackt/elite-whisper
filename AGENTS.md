# AGENTS.md

## Project

Elite Whisper is a Windows desktop AI dictation app (WPF, .NET 8). It captures audio via hotkey, transcribes using local or cloud speech engines, then injects text into the active window. LLM providers (Ollama, Gemini, OpenRouter, local llama.cpp) provide optional post-processing.

## Build & Run

```bash
dotnet build
dotnet run
```

### Release packaging

```powershell
pwsh -NoProfile -File .\publish.ps1
```

`publish.ps1` is the only supported way to produce a release build. It runs five fail-loud phases and stops at the first failure:

1. `dotnet publish` - self-contained `net8.0-windows` / `win-x64` output into `build\publish`
2. pinned llama.cpp runtime - `download_llama.ps1` fetches the digest-verified release named in `runtime\llamacpp.json` into `build\runtime`
3. staging - app + runtime + third-party notices are assembled into `build\staging`
4. verification - the staged tree is checked (self-contained .NET runtime, current application resources, the exact llama.cpp file set, no developer artefacts or unrelated tools)
5. Inno Setup - `EliteWhisper_Setup.iss` packages the whole verified staging tree into `Output\EliteWhisper_Setup.exe`

Useful switches: `-SkipRuntimeProvisioning` (reuse the staged runtime), `-SkipInstaller` (assemble and verify only), `-Configuration`, `-RuntimeIdentifier`.

- Target: `net8.0-windows`, `win-x64`, self-contained
- No test projects, no lint/typecheck config, no CI
- Single project, no solution file: `EliteWhisper.csproj`
- Inno Setup script: `EliteWhisper_Setup.iss`; the installer payload is exactly the verified staging tree

## Architecture

- **MVVM** with `CommunityToolkit.Mvvm` (source-generated `ObservableObject`, `RelayCommand`)
- **DI**: `Microsoft.Extensions.Hosting` — all services registered in `App.xaml.cs` `ConfigureServices`
- **Entry point**: `Program.Main` → `App` → `OnStartup` → wizard (first run) → `MainWindow` + tray icon
- **Speech engines** (`Services/Speech/`): `SherpaOnnxEngine` (primary, uses `org.k2fsa.sherpa.onnx` NuGet), `WhisperEngine` (fallback), selected by `SpeechEngineSelector`
- **LLM providers** (`Services/LLM/`): `OllamaProvider`, `GeminiProvider`, `OpenRouterProvider`, `LocalLlmProvider` — all implement `ILlmProvider`, orchestrated by `LlmService`
- **llama.cpp**: `runtime/llamacpp.json` pins the upstream tag, commit, asset and per-file SHA-256 digests. `download_llama.ps1` downloads that exact asset (size + digest verified, no moving `latest` pointer) and stages it into `build\runtime`; the release build ships it next to the app as `llama.exe` plus the llama.cpp libraries. `LlamaCppService` resolves the executable from the install directory, `runtimes\win-x64\native\`, or the development `dist\` folder.
- **Native interop** (`Native/Win32.cs`): P/Invoke for `SetWindowLong`, `SetWindowPos`, `GetForegroundWindow` — used for topmost/no-activate widget window
- **Key services**: `DictationService` (orchestrator), `HotkeyService`, `AudioCaptureService` (NAudio), `TextInjectionService`, `AIEngineService`, `WhisperConfigurationService`
- **Resources**: `Resources/models.json` and `Resources/parakeet_vocab.json` are copied to output directory and digest-verified against the repository copies during packaging

## Conventions

- Nullable enabled, implicit usings enabled
- All services are singletons unless explicitly transient (ViewModels are transient)
- Logging: `debug_startup.txt` in app base directory (best-effort, no framework)
- Updates: `NetSparkleUpdater` with `appcast_template.xml`. The updater is fail-closed: `SecurityMode.Strict`, no signing key configured and no public key file, so a downloaded update is never installed until signing is configured. Remote update checks are off unless `RemoteUpdatesEnabled` is set. The build does not generate an `appcast.xml`.
- `.gitignore` excludes `dist_v*/` except `dist_v5/EliteWhisper.exe`, plus the packaging output (`build/`, `Output/`) and the SDK's WPF markup-compile temporary project (`*_wpftmp.csproj`)

## P0-3: Explicit AI Enhancement Outcomes (Completed)

The silent-fallback bug where Elite Whisper inserted raw transcription after an enhancement failure but reported "Done!" has been fixed. Key changes:

- **New types**: `Models/PostProcessingResult.cs` with `PostProcessingOutcome` enum (`Disabled`, `Succeeded`, `NoProvider`, `ProviderFailed`, `TimedOut`, `RateLimited`) — **no `Cancelled` member**; cancellation propagates as `OperationCanceledException`
- **Status mapping**: `Services/DictationStatusMessages.cs` — honest widget statuses (`Succeeded`/`Disabled` → "Done!"; failures → "Transcribed; enhancement failed/timed out/rate limited"; `NoProvider` → "Transcribed; no AI provider")
- **Rewritten service**: `Services/PostProcessingService.cs` returns `Task<PostProcessingResult>` with explicit classification (`ClassifyCancellation`, `ContainsTimeout`), preserved fallback policy (only on `LlmRateLimitException`, order Gemini → Ollama), cancellation never swallowed, empty fallback replies treated as failed attempts
- **Updated orchestrator**: `Services/DictationService.cs` injects result text once, uses `ForPostProcessing(enhancement)` for status, 1500 ms failure / 800 ms success display, records `EnhancementSucceeded` (nullable) and `EnhancementProvider` in history
- **History model**: `Models/DictationRecord.cs` adds `EnhancementSucceeded?` and `EnhancementProvider` (backward compatible)

Builds clean on both Debug and Release.

## Harness / Validation (P0-3)

No test project exists. Use a throwaway console harness **outside the repo** (e.g., `C:\Users\Tapan\AppData\Local\Temp\opencode\ew-p0-3-harness\`):

- Project: `OutputType Exe`, `net8.0-windows`, `UseWPF`, `ProjectReference` to `E:\Elite Whisper\EliteWhisper.csproj`
- Stub `HttpMessageHandler` per provider (response JSON property `response`)
- `WhisperConfigurationService()` is parameterless, reads `%LOCALAPPDATA%\EliteWhisper\whisper_config.json`, only reads on construct — **never call `SaveConfiguration`**
- Fake Gemini key must be non-Base64 (e.g., `fake-key-not-base64`)
- `OllamaProvider.IsAvailable` hardcoded `true`; `NoProvider` unreachable in production
- Set `mode.PostProcess.PreferredProvider` explicitly for deterministic selection
- 9 cases: success, provider failure, timeout, fallback success, fallback exhaustion, cancellation, disabled, empty fallback reply, empty primary reply
- Assert: outcome, `ProviderName`, status ≠ "Done!" on failures, `Text == raw` on failures
- Case 3 probes .NET 8 HttpClient timeout exception shape; adjust `ClassifyCancellation` if inner `TimeoutException` marker absent; rebuild Debug + Release if production code changed

## Agent Workflow Notes

- **Superpowers harness**: `using-superpowers` is loaded — do not re-invoke. Invoke relevant skills before actions and announce them.
- **OpenCode mapping**: todos → `todowrite`, edits → `apply_patch`, shell → `bash`, search → `grep`/`glob`
- **Safety**: read `AGENTS.md` and `git status --short` first; no reset/revert/stash/stage/commit/push; no unrelated refactors; do not modify P0-1 updater files or P0-2 installer/publish/runtime artifacts
- **Parallel workstream**: `Services/HttpConnectivity.cs`, `dist/` llama.cpp DLLs, SharpCompress 0.48.0, cancellation propagation, local-LLM provider rework — **do not touch**
- **Verification**: run `dotnet build EliteWhisper.csproj -c Debug` and `-c Release` (expect 0 errors, 6 pre-existing warnings: CS0618 at `LlmService.cs:70`/`ModeService.cs:47`, CS8602 at `ModesViewModel.cs:171`)

## Key Files Reference

| File | Purpose |
|------|---------|
| `EliteWhisper.csproj` | Build config, package refs |
| `publish.ps1` / `download_llama.ps1` | Release pipeline (fail-loud, digest-verified) |
| `runtime/llamacpp.json` | Pinned llama.cpp tag `b11669`, commit `620d222`, asset SHA-256 `37b3efc...`, 23 files |
| `EliteWhisper_Setup.iss` | Inno Setup; compile-time guard for staging completeness |
| `Services/PostProcessingService.cs` | Explicit outcome logic, fallback, classification |
| `Services/DictationService.cs` | Orchestrator, widget status, history |
| `Services/DictationStatusMessages.cs` | Outcome → widget text mapping |
| `Models/PostProcessingResult.cs` | Result type with `Outcome` enum |
| `Models/DictationRecord.cs` | History record with enhancement provenance |
| `THIRD-PARTY-NOTICES.md` | Verified licenses for redistributed components |

## Existing Instruction Files

- `E:\Elite Whisper\AGENTS.md` (this file)
- Check for `CLAUDE.md`, `.cursor/rules/`, `.cursorrules`, `.github/copilot-instructions.md`, `opencode.json` — prefer executable sources (scripts, config) over prose