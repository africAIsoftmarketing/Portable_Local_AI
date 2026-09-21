@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: Rôle    : alias legacy → start-windows.bat (workflow Studio officiel).
:: Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
:: ─────────────────────────────────────────────────────────────────────────────
echo.
echo   [i] start.bat (legacy PortableAI) — delegation vers start-windows.bat
echo.
call "%~dp0start-windows.bat" %*
exit /b %ERRORLEVEL%
