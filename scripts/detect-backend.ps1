# ─────────────────────────────────────────────────────────────────────────────
# Rôle    : détection du backend d'inférence sous Windows (PowerShell 5.1+).
#           Sortie clé=valeur (BACKEND=..., REASON=...).
# Auteur  : AfricAIsoft
# Licence : MIT
# Date    : 2026-08-24
# Version : 0.6.1 (2026-09-24) - cle "windows" (nommage verrouille de la
#           master copy), repli sur l ancien bin/windows-x86_64/ s il est
#           seul present. Detection GPU inchangee.
# ─────────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = "SilentlyContinue"
$StudioRoot = Split-Path -Parent $PSScriptRoot
# Nommage verrouille : bin/windows/. Repli retrocompatible sur l ancien
# bin/windows-x86_64/ uniquement s il est le seul present.
$PlatKey = "windows"
if (-not (Test-Path "$StudioRoot/bin/windows") -and (Test-Path "$StudioRoot/bin/windows-x86_64")) {
    $PlatKey = "windows-x86_64"
}

function Test-Binary($backend) {
    Test-Path "$StudioRoot/bin/$PlatKey/$backend/llama-server.exe"
}

function Test-Cmd($cmd, [string[]]$args, $timeoutSec = 1) {
    try {
        $job = Start-Job -ScriptBlock { param($c, $a) & $c @a *>&1 } -ArgumentList $cmd, $args
        $ok = Wait-Job $job -Timeout $timeoutSec
        if ($ok) {
            $out = Receive-Job $job
            Remove-Job $job -Force
            return @{ Ok = ($LASTEXITCODE -eq 0 -or $out); Out = ($out -join "`n") }
        }
        Stop-Job $job -Force; Remove-Job $job -Force
        return @{ Ok = $false; Out = "timeout" }
    } catch { return @{ Ok = $false; Out = "err" } }
}

$Backend = "cpu"
$Reason = "fallback aucun GPU détecté"
$GpuLayers = 0

# CUDA
$r = Test-Cmd "nvidia-smi" @("--query-gpu=name","--format=csv,noheader")
if ($r.Ok -and $r.Out.Trim() -and (Test-Binary "cuda")) {
    $Backend = "cuda"; $Reason = "nvidia-smi ok"; $GpuLayers = 999
}

# Vulkan
if ($Backend -eq "cpu") {
    $r = Test-Cmd "vulkaninfo" @("--summary")
    if ($r.Ok -and ($r.Out -match "DISCRETE_GPU|INTEGRATED_GPU") -and (Test-Binary "vulkan")) {
        $Backend = "vulkan"; $Reason = "vulkaninfo ok"; $GpuLayers = 999
    }
}

Write-Output "PLATFORM=$PlatKey"
Write-Output "BACKEND=$Backend"
Write-Output "REASON=$Reason"
Write-Output "GPU_LAYERS=$GpuLayers"
