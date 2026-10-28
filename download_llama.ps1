[CmdletBinding()]
param(
    # Destination directory for the staged runtime. Defaults to <repo>\build\runtime.
    [string]$Destination,
    # Path to the pinned runtime manifest. Defaults to <repo>\runtime\llamacpp.json.
    [string]$ManifestPath,
    # Directory used to cache the downloaded archive between runs. Defaults to <repo>\build\cache.
    [string]$CacheDirectory,
    # Re-download the archive even when a cached copy is present.
    [switch]$NoCache
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Provisions the llama.cpp CPU runtime used by the built-in local LLM provider.
#
# The upstream release layout is pinned in runtime\llamacpp.json: an immutable
# nightly tag, the exact release asset, its SHA-256 digest, and the digest of
# every file that must be shipped. Nothing here is resolved from a moving
# "latest" pointer, and every staged byte is verified against the manifest
# before it is accepted.
# ---------------------------------------------------------------------------

$repoRoot = $PSScriptRoot
if (-not $Destination)        { $Destination      = Join-Path $repoRoot "build\runtime" }
if (-not $ManifestPath)       { $ManifestPath     = Join-Path $repoRoot "runtime\llamacpp.json" }
if (-not $CacheDirectory)     { $CacheDirectory   = Join-Path $repoRoot "build\cache" }

function Fail([string]$message) {
    Write-Host ""
    Write-Host "ERROR: $message" -ForegroundColor Red
    Write-Host ""
    exit 1
}

function Get-Sha256([string]$path) {
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------------------------------------------------------------- manifest --
if (-not (Test-Path -LiteralPath $ManifestPath)) {
    Fail "Runtime manifest not found: $ManifestPath"
}
try {
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
} catch {
    Fail "Runtime manifest is not valid JSON: $ManifestPath ($($_.Exception.Message))"
}

foreach ($field in @("asset", "upstream", "requiredFiles")) {
    if ($null -eq $manifest.$field) { Fail "Runtime manifest is missing the '$field' section: $ManifestPath" }
}
if (-not $manifest.asset.url)      { Fail "Runtime manifest does not pin an asset URL." }
if (-not $manifest.asset.sha256)   { Fail "Runtime manifest does not pin an asset SHA-256 digest." }
if (-not $manifest.asset.name)     { Fail "Runtime manifest does not pin an asset name." }
if (-not $manifest.upstream.tag)   { Fail "Runtime manifest does not pin an upstream release tag." }
if (-not $manifest.requiredFiles -or @($manifest.requiredFiles).Count -eq 0) {
    Fail "Runtime manifest does not list any required runtime files."
}
if ($manifest.asset.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
    Fail "Runtime manifest asset digest is not a valid SHA-256 value: $($manifest.asset.sha256)"
}

$expectedArchiveHash = $manifest.asset.sha256.ToLowerInvariant()
$expectedArchiveSize = [int64]$manifest.asset.size
$assetName           = $manifest.asset.name
$assetUrl            = $manifest.asset.url
$tag                 = $manifest.upstream.tag

Write-Host "llama.cpp runtime"
Write-Host "  manifest      : $ManifestPath"
Write-Host "  pinned tag    : $tag"
Write-Host "  asset         : $assetName"
Write-Host "  asset sha256  : $expectedArchiveHash"
Write-Host "  required files: $(@($manifest.requiredFiles).Count)"

# ------------------------------------------------- optional API cross-check --
# The manifest is the source of truth. If the GitHub API is reachable we also
# confirm that the publisher still advertises the same digest for the pinned
# tag. A disagreement is a hard failure - we never fall back to another asset.
try {
    $apiHeaders = @{ "User-Agent" = "EliteWhisper-Build"; "Accept" = "application/vnd.github+json" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$($manifest.upstream.repository)/releases/tags/$tag" -Headers $apiHeaders
    $remoteAsset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if ($null -eq $remoteAsset) {
        Write-Host "  cross-check   : SKIPPED - upstream no longer lists '$assetName' on tag '$tag'."
    } elseif ($remoteAsset.digest -and $remoteAsset.digest -notmatch "^sha256:$expectedArchiveHash$") {
        Fail ("Upstream digest for '$assetName' on tag '$tag' does not match the manifest." +
              " Upstream: $($remoteAsset.digest)  Manifest: sha256:$expectedArchiveHash")
    } else {
        Write-Host "  cross-check   : OK - upstream advertises the same digest."
    }
} catch {
    Write-Host "  cross-check   : SKIPPED - GitHub API unavailable ($($_.Exception.Message))."
}

# ------------------------------------------------------------- download ------
if (-not (Test-Path -LiteralPath $CacheDirectory)) {
    New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
}
$archivePath = Join-Path $CacheDirectory $assetName

if ($NoCache -and (Test-Path -LiteralPath $archivePath)) {
    Remove-Item -LiteralPath $archivePath -Force
}

if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Host ""
    Write-Host "Downloading $assetUrl ..."
    try {
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest -Uri $assetUrl -OutFile $archivePath -UseBasicParsing
    } catch {
        Fail "Could not download the pinned release asset: $assetUrl ($($_.Exception.Message))"
    }
} else {
    Write-Host ""
    Write-Host "Using cached archive: $archivePath"
}

# ------------------------------------------------- archive integrity check --
$actualArchiveSize = (Get-Item -LiteralPath $archivePath).Length
if ($actualArchiveSize -ne $expectedArchiveSize) {
    Fail ("Downloaded archive size mismatch for '$assetName'. Expected $expectedArchiveSize bytes, got $actualArchiveSize.")
}
$actualArchiveHash = Get-Sha256 $archivePath
Write-Host "  expected sha256: $expectedArchiveHash"
Write-Host "  actual   sha256: $actualArchiveHash"
if ($actualArchiveHash -ne $expectedArchiveHash) {
    Fail "SHA-256 mismatch for '$assetName'. The download does not match the pinned digest. Nothing was staged."
}
Write-Host "  archive integrity verified."

# -------------------------------------------------------------- extract ------
$workDir = Join-Path ([IO.Path]::GetTempPath()) ("llamacpp_" + [Guid]::NewGuid().ToString("N"))
$extractPath = Join-Path $workDir "extract"
New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

try {
    Write-Host ""
    Write-Host "Extracting ..."
    try {
        Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force
    } catch {
        Fail "Could not extract '$assetName'. The archive may be incomplete or corrupt."
    }

    $archiveFiles = @(Get-ChildItem -LiteralPath $extractPath -Recurse -File)
    if ($archiveFiles.Count -eq 0) {
        Fail "Extracted archive '$assetName' is empty."
    }

    # ------------------------------------------------------------- stage -----
    if (-not (Test-Path -LiteralPath $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }
    $staged = @{}
    foreach ($required in $manifest.requiredFiles) {
        $source = $required.source
        if (-not $source) { $source = $required.name }

        $match = $archiveFiles | Where-Object { $_.Name -eq $source } | Select-Object -First 1
        if ($null -eq $match) {
            Fail "Required runtime file '$source' (staged as '$($required.name)') is not present in '$assetName'."
        }
        if ($required.sha256 -and $required.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            Fail "Manifest digest for '$($required.name)' is not a valid SHA-256 value."
        }
        $actual = Get-Sha256 $match.FullName
        if ($actual -ne $required.sha256.ToLowerInvariant()) {
            Fail ("Digest mismatch for '$source' inside '$assetName'." +
                  " Expected $($required.sha256), got $actual. Nothing was staged.")
        }
        if ($required.size -and $match.Length -ne [int64]$required.size) {
            Fail "Size mismatch for '$source' inside '$assetName'. Expected $($required.size), got $($match.Length)."
        }

        $target = Join-Path $Destination $required.name
        Copy-Item -LiteralPath $match.FullName -Destination $target -Force
        $staged[$required.name] = $true
    }

    # ------------------------------------------------------ license files ----
    foreach ($license in @($manifest.licenseFiles)) {
        if ($null -eq $license) { continue }
        $match = $archiveFiles | Where-Object { $_.Name -eq $license.name } | Select-Object -First 1
        if ($null -eq $match) {
            Fail "Required license file '$($license.name)' is not present in '$assetName'."
        }
        if ($license.sha256) {
            $actual = Get-Sha256 $match.FullName
            if ($actual -ne $license.sha256.ToLowerInvariant()) {
                Fail "Digest mismatch for license file '$($license.name)'. Expected $($license.sha256), got $actual."
            }
        }
        $licenseDir = Join-Path $Destination "licenses"
        if (-not (Test-Path -LiteralPath $licenseDir)) {
            New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
        }
        Copy-Item -LiteralPath $match.FullName -Destination (Join-Path $licenseDir $license.name) -Force
    }

    # ------------------------------------------------- final verification ----
    $problems = @()
    foreach ($required in $manifest.requiredFiles) {
        $target = Join-Path $Destination $required.name
        if (-not (Test-Path -LiteralPath $target)) {
            $problems += "missing: $($required.name)"
            continue
        }
        $actual = Get-Sha256 $target
        if ($actual -ne $required.sha256.ToLowerInvariant()) {
            $problems += "digest mismatch: $($required.name)"
        }
    }
    if ($problems.Count -gt 0) {
        Fail ("Staged runtime failed verification in '$Destination': " + ($problems -join "; "))
    }

    # Report (but do not delete) anything unexpected in the destination.
    $extras = @(Get-ChildItem -LiteralPath $Destination -File -ErrorAction SilentlyContinue |
                Where-Object { -not $staged.ContainsKey($_.Name) })
    if ($extras.Count -gt 0) {
        Write-Host ""
        Write-Host "NOTE: $($extras.Count) file(s) in the destination are not part of the pinned runtime and were left untouched:"
        foreach ($extra in $extras) { Write-Host "  $($extra.Name)" }
        Write-Host "      The publish pipeline copies only manifest-listed files into the installer staging directory."
    }

    Write-Host ""
    Write-Host "Staged $(@($manifest.requiredFiles).Count) runtime files in $Destination"
    Write-Host "Done. llama.cpp $tag / $assetName"
}
finally {
    if (Test-Path -LiteralPath $workDir) {
        Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
