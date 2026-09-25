@echo off
rem AfricAIsoft Portable Studio - diagnostic des skills MCP (1.0.6)
rem Lancer depuis la cle : double-clic, ou  scripts\mcp-selftest.bat
setlocal
set "ROOT=%~dp0.."
set "PY=%ROOT%\bin\windows\python\python.exe"
if not exist "%PY%" set "PY=%ROOT%\bin\windows-x86_64\python\python.exe"
if not exist "%PY%" set "PY=python"
"%PY%" "%ROOT%\scripts\mcp-selftest.py" %*
echo.
pause
