[CmdletBinding()]
param(
    # Build configuration passed to `dotnet publish`.
    [string]$Configuration = "Release",
    # Runtime identifier passed to `dotnet publish`.
    [string]$RuntimeIdentifier = "win-x64",
    # Root of the pipeline's build tree. Defaults to <repo>\build.
    [string]$BuildRoot,
    # Final installer staging directory. Defaults to <BuildRoot>\staging.
    [string]$StagingDirectory,
    # Reuse the already-staged llama.cpp runtime instead of provisioning it again.
    [switch]$SkipRuntimeProvisioning,
    # Assemble the staging directory but do not invoke the Inno Setup compiler.
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Reproducible publish + packaging pipeline for Elite Whisper.
#
#   1. publish the app  : dotnet publish -> build\publish   (self-contained)
#   2. provision runtime: pinned llama.cpp -> build\runtime   (digest verified)
#   3. assemble staging : build\publish + build\runtime -> build\staging
#   4. verify staging   : self-contained, current resources, exact runtime set
#   5. compile installer: Inno Setup consumes build\staging only
#
# Every step fails loudly. The pipeline never touches model directories, user
# data, or any tracked source file.
# ---------------------------------------------------------------------------

$repoRoot = $PSScriptRoot
if (-not $BuildRoot)        { $BuildRoot        = Join-Path $repoRoot "build" }
if (-not $StagingDirectory) { $StagingDirectory = Join-Path $BuildRoot "staging" }

$publishDir   = Join-Path $BuildRoot "publish"
$runtimeDir   = Join-Path $BuildRoot "runtime"
$manifestPath = Join-Path $repoRoot "runtime\llamacpp.json"
$issPath      = Join-Path $repoRoot "EliteWhisper_Setup.iss"
$noticesPath  = Join-Path $repoRoot "THIRD-PARTY-NOTICES.md"
$projectPath  = Join-Path $repoRoot "EliteWhisper.csproj"

function Write-Section([string]$title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

function Fail([string]$message) {
    Write-Host ""
    Write-Host "ERROR: $message" -ForegroundColor Red
    Write-Host ""
    exit 1
}

function Get-Sha256([string]$path) {
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Find-InnoCompiler {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $candidates = @($candidates)
    if ($candidates.Count -gt 0) { return $candidates[0] }
    $onPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

# ------------------------------------------------------------------ inputs --
foreach ($required in @($manifestPath, $projectPath, $issPath, $noticesPath)) {
    if (-not (Test-Path -LiteralPath $required)) { Fail "Required input not found: $required" }
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

Write-Section "Inputs"
Write-Host "  project         : $projectPath"
Write-Host "  configuration   : $Configuration"
Write-Host "  runtime id      : $RuntimeIdentifier"
Write-Host "  staging target  : $StagingDirectory"
Write-Host "  llama.cpp tag   : $($manifest.upstream.tag)"

# ------------------------------------------------------- 1. publish the app --
Write-Section "1/5  dotnet publish (self-contained)"
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$publishArgs = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained",
    "-o", $publishDir
)
Write-Host "  dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { Fail "'dotnet publish' failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath (Join-Path $publishDir "EliteWhisper.exe"))) {
    Fail "'dotnet publish' did not produce EliteWhisper.exe in $publishDir."
}
Write-Host "  published OK."

# ------------------------------------------ 2. provision the pinned runtime --
Write-Section "2/5  llama.cpp runtime (pinned + digest verified)"
if ($SkipRuntimeProvisioning) {
    Write-Host "  -SkipRuntimeProvisioning: using the already-staged runtime in $runtimeDir"
} else {
    & (Join-Path $repoRoot "download_llama.ps1") -Destination $runtimeDir -ManifestPath $manifestPath
    if ($LASTEXITCODE -ne 0) { Fail "download_llama.ps1 failed with exit code $LASTEXITCODE." }
}

# ------------------------------------------- 3. assemble the staging tree --
Write-Section "3/5  assembling the installer staging directory"
if (Test-Path -LiteralPath $StagingDirectory) {
    Remove-Item -LiteralPath $StagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingDirectory -Force | Out-Null

# Application publish output, minus developer artefacts and runtime logs.
$excludedFromPublish = @("*.pdb", "*.log", "debug_startup.txt", "*.tmp", "*.user", "*.xml.user")
$copiedApp = 0
Get-ChildItem -LiteralPath $publishDir -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($publishDir.Length).TrimStart('\', '/')
    $skip = $false
    foreach ($pattern in $excludedFromPublish) {
        if ($_.Name -like $pattern) { $skip = $true; break }
    }
    if ($skip) { return }
    $target = Join-Path $StagingDirectory $relative
    $targetDir = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    $script:copiedApp++
}
Write-Host "  copied $copiedApp application file(s) from the publish output."

# The pinned llama.cpp runtime: only the files listed in the manifest.
$copiedRuntime = 0
foreach ($required in $manifest.requiredFiles) {
    $source = Join-Path $runtimeDir $required.name
    if (-not (Test-Path -LiteralPath $source)) {
        Fail "Pinned runtime file '$($required.name)' is missing from $runtimeDir."
    }
    $actual = Get-Sha256 $source
    if ($actual -ne $required.sha256.ToLowerInvariant()) {
        Fail "Pinned runtime file '$($required.name)' failed digest verification (expected $($required.sha256), got $actual)."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $StagingDirectory $required.name) -Force
    $copiedRuntime++
}
Write-Host "  copied $copiedRuntime verified llama.cpp runtime file(s)."

# Third-party license notices and the verbatim upstream license file.
Copy-Item -LiteralPath $noticesPath -Destination (Join-Path $StagingDirectory "THIRD-PARTY-NOTICES.md") -Force
$runtimeLicenseDir = Join-Path $runtimeDir "licenses"
if (Test-Path -LiteralPath $runtimeLicenseDir) {
    $stagingLicenseDir = Join-Path $StagingDirectory "licenses"
    New-Item -ItemType Directory -Path $stagingLicenseDir -Force | Out-Null
    Get-ChildItem -LiteralPath $runtimeLicenseDir -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stagingLicenseDir $_.Name) -Force
    }
    Write-Host "  copied the upstream license file(s)."
}

# ------------------------------------------------------- 4. verify staging --
Write-Section "4/5  verifying the staging directory"
$problems = @()

# The two executables the installer and the app depend on.
foreach ($exe in @("EliteWhisper.exe", "llama.exe")) {
    if (-not (Test-Path -LiteralPath (Join-Path $StagingDirectory $exe))) {
        $problems += "missing required executable: $exe"
    }
}

# Self-contained: the runtime must carry its own .NET runtime.
foreach ($core in @("coreclr.dll", "hostfxr.dll", "System.Private.CoreLib.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $StagingDirectory $core))) {
        $problems += "not self-contained: missing $core"
    }
}
$runtimeConfigPath = Join-Path $StagingDirectory "EliteWhisper.runtimeconfig.json"
if (Test-Path -LiteralPath $runtimeConfigPath) {
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw
    if ($runtimeConfig -match '"frameworks"') {
        $problems += "not self-contained: runtimeconfig.json declares a framework-dependent host"
    }
}
# The application manifests the host needs to start. Without them the installed
# app cannot resolve its runtime, so they are verified here rather than
# discovered at first launch on a user's machine.
foreach ($appManifest in @("EliteWhisper.deps.json", "EliteWhisper.runtimeconfig.json")) {
    if (-not (Test-Path -LiteralPath (Join-Path $StagingDirectory $appManifest))) {
        $problems += "missing application manifest: $appManifest (the installed app cannot start without it)"
    }
}

# Current application resources: the staged copies must match the repo sources.
foreach ($resource in @("models.json", "parakeet_vocab.json")) {
    $source = Join-Path $repoRoot "Resources\$resource"
    $staged = Join-Path $StagingDirectory "Resources\$resource"
    if (-not (Test-Path -LiteralPath $staged)) {
        $problems += "missing application resource: Resources\$resource"
        continue
    }
    if (Test-Path -LiteralPath $source) {
        $sourceHash = Get-Sha256 $source
        $stagedHash = Get-Sha256 $staged
        if ($sourceHash -ne $stagedHash) {
            $problems += "stale application resource: Resources\$resource does not match the repository copy"
        }
    }
}

# The llama.cpp set must be exactly the manifest set - nothing more.
$stagedLlamaDlls = @(Get-ChildItem -LiteralPath $StagingDirectory -File |
    Where-Object { $_.Name -match '^(llama|ggml|mtmd|libomp)' } | ForEach-Object { $_.Name })
$expectedLlamaDlls = @($manifest.requiredFiles | ForEach-Object { $_.name })
foreach ($unexpected in $stagedLlamaDlls) {
    if ($unexpected -notin $expectedLlamaDlls) {
        $problems += "unexpected llama.cpp file in staging: $unexpected"
    }
}
foreach ($expected in $expectedLlamaDlls) {
    if ($expected -notin $stagedLlamaDlls) {
        $problems += "missing llama.cpp file in staging: $expected"
    }
}

# Nothing that should never be shipped.
$forbidden = @(
    "*.pdb", "*.log", "debug_startup.txt",
    "llama-bench*", "llama-batched-bench*", "llama-completion*", "llama-perplexity*",
    "llama-quantize*", "llama-fit-params*", "ggml-rpc*", "libomp140*",
    "llama-server.exe", "llama-tts.exe", "llama-imatrix.exe", "llama-tokenize.exe",
    "llama-results.exe", "llama-gguf-split.exe", "llama-gemma3-cli.exe", "llama-llava-cli.exe",
    "llama-minicpmv-cli.exe", "llama-mtmd-cli.exe", "llama-mtmd-debug.exe", "llama-qwen2vl-cli.exe"
)
Get-ChildItem -LiteralPath $StagingDirectory -Recurse -File | ForEach-Object {
    foreach ($pattern in $forbidden) {
        if ($_.Name -like $pattern) { $problems += "forbidden file in staging: $($_.FullName.Substring($StagingDirectory.Length).TrimStart('\'))" }
    }
}

# Third-party notices must be present.
if (-not (Test-Path -LiteralPath (Join-Path $StagingDirectory "THIRD-PARTY-NOTICES.md"))) {
    $problems += "missing THIRD-PARTY-NOTICES.md in staging"
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "  - $problem" -ForegroundColor Red }
    Fail "Staging verification failed with $($problems.Count) problem(s)."
}

$stagedFileCount = @(Get-ChildItem -LiteralPath $StagingDirectory -Recurse -File).Count
$stagedBytes = (Get-ChildItem -LiteralPath $StagingDirectory -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "  EliteWhisper.exe        : present"
Write-Host "  llama.exe               : present"
Write-Host "  self-contained .NET     : present (coreclr.dll, hostfxr.dll, System.Private.CoreLib.dll)"
Write-Host "  Resources               : current (models.json, parakeet_vocab.json match the repository)"
Write-Host "  llama.cpp runtime       : $($expectedLlamaDlls.Count) file(s), digests match the manifest"
Write-Host "  third-party notices     : present"
Write-Host ("  staged total            : {0} file(s), {1:N1} MB" -f $stagedFileCount, ($stagedBytes / 1MB))

# --------------------------------------------------- 5. compile installer --
Write-Section "5/5  Inno Setup"
if ($SkipInstaller) {
    Write-Host "  -SkipInstaller: the installer was not compiled."
} else {
    # The installer may only consume the freshly assembled staging directory.
    foreach ($exe in @("EliteWhisper.exe", "llama.exe")) {
        if (-not (Test-Path -LiteralPath (Join-Path $StagingDirectory $exe))) {
            Fail "Refusing to run the Inno Setup compiler: '$exe' is not available in $StagingDirectory."
        }
    }
    $iscc = Find-InnoCompiler
    if (-not $iscc) {
        Write-Host "  ISCC.exe was not found. Skipping the installer compile." -ForegroundColor Yellow
        Write-Host "  The staging directory is complete and ready for 'ISCC.exe $issPath'."
    } else {
        Write-Host "  compiler: $iscc"
        # The absolute staging path is passed to the script so the installer can
        # only ever consume the directory this run just assembled. The value is
        # deliberately NOT quoted: Inno Setup's /D would keep the quotes and the
        # preprocessor would produce a broken path.
        $stagingForCompiler = (Resolve-Path -LiteralPath $StagingDirectory).Path
        Write-Host "  staging  : $stagingForCompiler"
        Push-Location $repoRoot
        try {
            & $iscc "/DBuildDir=$stagingForCompiler" $issPath
            $isccExit = $LASTEXITCODE
        } finally {
            Pop-Location
        }
        if ($isccExit -ne 0) { Fail "Inno Setup compilation failed with exit code $isccExit." }
        $installer = Join-Path $repoRoot "Output\EliteWhisper_Setup.exe"
        if (-not (Test-Path -LiteralPath $installer)) {
            Fail "Inno Setup reported success but '$installer' was not produced."
        }
        Write-Host ("  installer: {0} ({1:N1} MB)" -f $installer, ((Get-Item $installer).Length / 1MB))
    }
}

Write-Host ""
Write-Host "Publish pipeline completed successfully." -ForegroundColor Green
exit 0
