# build-portable.ps1 — Version PowerShell (Windows) du script d'assemblage.
# Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
param(
    [switch]$DryRun,
    [string]$Target = "windows-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $root "dist\$Target"
$PyVer = "3.12.5"

$PyUrls = @{
    "windows-x64" = "https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-x86_64-pc-windows-msvc-install_only.tar.gz"
    "linux-x64"   = "https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-x86_64-unknown-linux-gnu-install_only.tar.gz"
    "linux-arm64" = "https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-aarch64-unknown-linux-gnu-install_only.tar.gz"
    "macos-x64"   = "https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-x86_64-apple-darwin-install_only.tar.gz"
    "macos-arm64" = "https://github.com/indygreg/python-build-standalone/releases/download/20240814/cpython-3.12.5+20240814-aarch64-apple-darwin-install_only.tar.gz"
}

function Log([string]$msg) { Write-Host "[build-portable] $msg" -ForegroundColor Green }
function Run([string]$cmd) {
    if ($DryRun) { Write-Host "  [dry-run] $cmd" } else { Invoke-Expression $cmd }
}

$pyUrl = $PyUrls[$Target]
if (-not $pyUrl) { throw "TARGET inconnu: $Target" }

Log "Target=$Target  Sortie=$out  DryRun=$DryRun"
Run "New-Item -ItemType Directory -Force -Path '$out\bin\$Target\python' | Out-Null"
Log "1/6  Python $PyVer ← $pyUrl"
Run "Invoke-WebRequest -Uri '$pyUrl' -OutFile \$env:TEMP\python-$Target.tar.gz"
Run "tar -C '$out\bin\$Target\python' --strip-components=1 -xzf \$env:TEMP\python-$Target.tar.gz"

Log "2/6  Wheels des dépendances"
Run "New-Item -ItemType Directory -Force -Path '$out\vendor\wheels' | Out-Null"
Run "python -m pip download -d '$out\vendor\wheels' -r '$root\backend\requirements.txt'"

Log "3/6  Binaires llama.cpp"
Run "powershell -File '$root\scripts\fetch-binaries.ps1' -Target $Target -Out '$out\bin\$Target'"

Log "4/6  Copie du code"
foreach ($d in @("app","config","ui","mcp-servers","scripts","docs","skills")) {
    Run "Copy-Item -Recurse -Force '$root\$d' '$out\'"
}
foreach ($f in @("README.md","LICENSE","VERSION","CHANGELOG.md","install.bat","start-windows.bat")) {
    if (Test-Path "$root\$f") { Run "Copy-Item '$root\$f' '$out\'" }
}

Log "5/6  Génération de release.json (SHA-256 par fichier)"
if (-not $DryRun) {
    $files = Get-ChildItem -Recurse -File $out | Where-Object { $_.Name -ne "release.json" -and -not $_.Name.EndsWith(".sig") }
    $entries = @()
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($out.Length + 1).Replace('\','/')
        $sha = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
        $entries += @{ path = $rel; size = $f.Length; sha256 = $sha }
    }
    $manifest = @{
        product = "AfricAIsoft Portable Studio"
        version = (Get-Content "$out\VERSION" -ErrorAction SilentlyContinue | Out-String).Trim()
        target = $Target
        python_version = $PyVer
        generated_at_utc = (Get-Date).ToUniversalTime().ToString("o")
        files = $entries
        total_files = $entries.Count
        total_bytes = ($entries | Measure-Object -Property size -Sum).Sum
    }
    $manifest | ConvertTo-Json -Depth 5 | Out-File "$out\release.json" -Encoding utf8
    Log "  release.json : $($manifest.total_files) fichiers, $($manifest.total_bytes) octets"
}

Log "6/6  Signature Ed25519 (optionnelle)"
if (Test-Path "$root\keys\private.pem" -and (-not $DryRun)) {
    Run "python '$root\scripts\sign-release.py' sign '$out\release.json' --key '$root\keys\private.pem'"
}

Log "Terminé. Livrable prêt dans : $out"
