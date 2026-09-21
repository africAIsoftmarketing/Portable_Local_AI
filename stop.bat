@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: Rôle    : arrêt propre du studio sur Windows.
:: Auteur  : AfricAIsoft
:: Licence : MIT
:: Date    : 2026-08-24
:: ─────────────────────────────────────────────────────────────────────────────
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo AfricAIsoft Portable Studio - arret

for %%f in ("data\pids\*.pid") do (
    set /p PID=<"%%f"
    if defined PID (
        echo   -^> kill !PID! ^(%%~nf^)
        taskkill /PID !PID! /T /F >nul 2>&1
    )
    del "%%f" >nul 2>&1
)

taskkill /IM llama-server.exe /F >nul 2>&1
taskkill /IM uvicorn.exe /F >nul 2>&1
taskkill /FI "IMAGENAME eq python.exe" /FI "WINDOWTITLE eq *uvicorn*" /F >nul 2>&1

:: Nettoyage staging
for /d %%d in ("%TEMP%\portableai_*") do rmdir /s /q "%%d" 2>nul

echo Arret termine.
endlocal
