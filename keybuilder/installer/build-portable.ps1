# build-portable.ps1 — Génère le ZIP portable AfricAIsoft Key Builder.
# Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
# Usage   : pwsh -File installer/build-portable.ps1 [-Configuration Release]

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.5.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish"
$zipPath    = Join-Path $root "artifacts\AfricAIsoft.KeyBuilder-$Version-portable-$Runtime.zip"

Write-Host "→ dotnet publish (Release, self-contained, $Runtime)..."
& dotnet publish (Join-Path $root "src\AfricAIsoft.KeyBuilder.Wpf\AfricAIsoft.KeyBuilder.Wpf.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Copie de la documentation et exemple batch.
Copy-Item (Join-Path $root "docs\USER-GUIDE.md") $publishDir
Copy-Item (Join-Path $root "batch-config.example.json") $publishDir

if (Test-Path $zipPath) { Remove-Item $zipPath }
Write-Host "→ Compression $zipPath ..."
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "OK : $zipPath"
