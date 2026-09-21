@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: Rôle    : alias legacy → scripts\fetch-binaries.ps1 (workflow Studio officiel).
:: Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
:: ─────────────────────────────────────────────────────────────────────────────
echo.
echo   [i] install.bat (legacy PortableAI) — delegation a scripts\fetch-binaries.ps1
echo       Pour l'installation Studio complete, preferez :
echo         pwsh -File scripts\fetch-binaries.ps1 -Platform windows-x86_64 -Backend cpu
echo         pwsh -File scripts\build-portable.ps1 -Target windows-x64
echo.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\fetch-binaries.ps1" %*
exit /b %ERRORLEVEL%
