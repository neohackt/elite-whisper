$ErrorActionPreference = "Stop"

$repo = "ggerganov/llama.cpp"
$releasesUrl = "https://api.github.com/repos/$repo/releases/latest"

Write-Host "Fetching latest release from $repo..."
$release = Invoke-RestMethod -Uri $releasesUrl -Headers @{"User-Agent" = "PowerShell"}
$version = $release.tag_name
Write-Host "Latest version: $version"

$asset = $release.assets | Where-Object { $_.name -like "*bin-win-cpu-x64.zip" } | Select-Object -First 1

if ($null -eq $asset) {
    # Fallback to avx2 if cpu not found (or just grab the first win-cpu-x64)
    Write-Error "Could not find a suitable asset matching *bin-win-cpu-x64.zip"
    exit 1
}

$downloadUrl = $asset.browser_download_url
$zipPath = "llama.zip"
$extractPath = "llama_temp"
$distPath = "dist"

Write-Host "Downloading $downloadUrl..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath

Write-Host "Extracting to $extractPath..."
if (Test-Path $extractPath) { Remove-Item -Recurse -Force $extractPath }
Expand-Archive -Path $zipPath -DestinationPath $extractPath

# Check structure - sometimes it's inside a 'bin' folder or root
$llamaExe = Get-ChildItem -Path $extractPath -Recurse -Filter "llama-cli.exe" -ErrorAction SilentlyContinue | Select-Object -First 1

if ($null -eq $llamaExe) {
    # Try looking for old name 'main.exe' or 'llama.exe' (llama.cpp changed names recently)
    # The new name is `llama-cli.exe` or just `llama-cli`.
    # But wait, looking at recent releases, it might be `llama-cli.exe`.
    # Let's check for any .exe that looks right.
    
    $llamaExe = Get-ChildItem -Path $extractPath -Recurse -Filter "llama-cli.exe" | Select-Object -First 1
    if ($null -eq $llamaExe) {
        $llamaExe = Get-ChildItem -Path $extractPath -Recurse -Filter "main.exe" | Select-Object -First 1
    }
     if ($null -eq $llamaExe) {
        $llamaExe = Get-ChildItem -Path $extractPath -Recurse -Filter "llama-main.exe" | Select-Object -First 1
    }
}

if ($null -eq $llamaExe) {
    Write-Error "Could not find llama-cli.exe or main.exe in the extracted archive."
    exit 1
}

Write-Host "Found executable: $($llamaExe.FullName)"

# Copy to dist as llama.exe
$dest = Join-Path $distPath "llama.exe"
Write-Host "Copying $($llamaExe.FullName) to $dest..."
Copy-Item -Path $llamaExe.FullName -Destination $dest -Force

# Copy DLLs if any (cudart, etc. - usually none for cpu build but maybe unchecked dependnecies)
# Specifically, llama.dll might be needed if it's a shared build.
# The bin-win releases usually are static or have dlls next to them.
$dlls = Get-ChildItem -Path $llamaExe.DirectoryName -Filter "*.dll"
foreach ($dll in $dlls) {
    $destDll = Join-Path $distPath $dll.Name
    Write-Host "Copying $($dll.Name)..."
    Copy-Item -Path $dll.FullName -Destination $destDll -Force
}

# Cleanup
Write-Host "Cleaning up..."
Remove-Item $zipPath -Force
Remove-Item $extractPath -Recurse -Force

Write-Host "Done!"
