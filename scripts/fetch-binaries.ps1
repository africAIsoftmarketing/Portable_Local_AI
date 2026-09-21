# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : téléchargement Windows des binaires llama.cpp.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# Usage   : scripts/fetch-binaries.ps1 [-Tag b11071] [-Backend cpu]
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string]$Tag = "b11071",
    [string]$Backend = "cpu",
    [string]$Platform = "windows-x86_64"
)

$ErrorActionPreference = "Stop"
$StudioRoot = Split-Path -Parent $PSScriptRoot

if ($Backend -ne "cpu") {
    Write-Warning "Backend $Backend non fetché en Phase 2 (voir docs). Utiliser 'cpu'."
    exit 0
}

$AssetPattern = "win-cpu-x64.zip"
Write-Host "Fetch llama.cpp $Tag pour $Platform ($Backend)"

$Release = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$Tag" `
    -Headers @{ "Accept" = "application/vnd.github+json" }

$Asset = $Release.assets | Where-Object { $_.name -like "*$AssetPattern*" -and $_.name -notmatch "cudart|vulkan|sycl|kompute|opencl" } | Select-Object -First 1
if (-not $Asset) { Write-Error "Asset introuvable : $AssetPattern"; exit 1 }

$DestDir = Join-Path $StudioRoot "bin\$Platform\$Backend"
New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
$Tmp = New-Item -ItemType Directory -Path (Join-Path $env:TEMP "portableai_fetch_$([guid]::NewGuid())") -Force

Write-Host "[*] Téléchargement $($Asset.name)"
$Zip = Join-Path $Tmp $Asset.name
Invoke-WebRequest -Uri $Asset.browser_download_url -OutFile $Zip

Write-Host "[*] Extraction"
Expand-Archive -Path $Zip -DestinationPath $Tmp -Force

# Flatten éventuel sous-dossier versionné
$Inner = Get-ChildItem -Path $Tmp -Directory | Where-Object { $_.Name -ne "extract" } | Select-Object -First 1
if ($Inner -and -not (Test-Path (Join-Path $Tmp "llama-server.exe"))) {
    Copy-Item -Recurse -Force -Path (Join-Path $Inner.FullName "*") -Destination $Tmp
}

# Copie finale
Get-ChildItem -Path $Tmp -File | Copy-Item -Destination $DestDir -Force

if (Test-Path (Join-Path $DestDir "llama-server.exe")) {
    Write-Host "[OK] Installé dans bin\$Platform\$Backend\"
} else {
    Write-Warning "llama-server.exe non trouvé dans l'archive"
}
Remove-Item -Recurse -Force $Tmp -ErrorAction SilentlyContinue
