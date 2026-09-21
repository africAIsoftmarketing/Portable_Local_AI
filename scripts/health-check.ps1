# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : health-check HTTP Windows.
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# Usage   : scripts/health-check.ps1 [-Port 8080] [-Prefix ""]
# ─────────────────────────────────────────────────────────────────────────────
param([int]$Port = 8080, [string]$Prefix = "")
$Url = "http://127.0.0.1:$Port$Prefix/health"
for ($i = 1; $i -le 30; $i++) {
    try {
        $r = Invoke-WebRequest -Uri $Url -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
        if ($r.StatusCode -eq 200) { Write-Output "OK $Url"; exit 0 }
    } catch { Start-Sleep -Seconds 1 }
}
Write-Output "KO $Url"; exit 1
