@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: Rôle    : launcher AfricAIsoft Portable Studio pour Windows.
::           Wrapper autour de scripts\core-startup.ps1.
:: Auteur  : AfricAIsoft
:: Licence : MIT
:: Date    : 2026-08-24
:: ─────────────────────────────────────────────────────────────────────────────
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo.
echo ============================================================
echo   AfricAIsoft Portable Studio - Windows
echo ============================================================

:: Vérif VC++ Redistributable (nécessaire pour llama-server Windows)
if not exist "%SystemRoot%\System32\VCRUNTIME140_1.dll" (
    echo [!] Composant manquant : VCRUNTIME140_1.dll
    echo     Installez Microsoft Visual C++ Redistributable :
    echo     https://aka.ms/vs/17/release/vc_redist.x64.exe
    pause
    exit /b 1
)

:: Résolution Python portable
set "PYTHON=%~dp0bin\windows-x86_64\python\python.exe"
if not exist "%PYTHON%" (
    :: Fallback sur Python système
    where python >nul 2>nul
    if !ERRORLEVEL! neq 0 (
        echo [!] Python introuvable. Installez Python 3.12 ou lancez scripts\setup-python.ps1
        pause
        exit /b 1
    )
    for /f "delims=" %%i in ('where python') do set "PYTHON=%%i" & goto :py_ok
    :py_ok
)

:: Détection backend GPU
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\detect-backend.ps1" > "%~dp0logs\backend.log" 2>&1

:: Extraction backend depuis les résultats
set "BACKEND=cpu"
for /f "tokens=1,* delims==" %%A in ('type "%~dp0logs\backend.log"') do (
    if /i "%%A"=="BACKEND" set "BACKEND=%%B"
)
echo [+] Backend : !BACKEND!

:: Bind + port depuis config
for /f "usebackq delims=" %%i in (`"%PYTHON%" -c "import json;print(json.load(open('config/settings.json'))['server']['bind_host'])"`) do set "BIND_HOST=%%i"
for /f "usebackq delims=" %%i in (`"%PYTHON%" -c "import json;print(json.load(open('config/settings.json'))['server']['port'])"`) do set "PORT=%%i"

echo [+] API : http://!BIND_HOST!:!PORT!

:: Résolution DLL runtime
set "BIN_DIR=%~dp0bin\windows-x86_64\%BACKEND%"
set "PATH=%BIN_DIR%;%PATH%"

:: Env pour orchestrateur
set "STUDIO_ROOT=%~dp0"
set "STUDIO_API_PREFIX="
set "STUDIO_BIND_HOST=!BIND_HOST!"
set "STUDIO_PORT=!PORT!"

:: PID pour arrêt propre
echo %random% > "%~dp0data\pids\orchestrator.pid"

:: Vérif dépendances Python
"%PYTHON%" -c "import fastapi,uvicorn,httpx,pydantic" 2>nul
if !ERRORLEVEL! neq 0 (
    if exist "%~dp0bin\windows-x86_64\python\wheels" (
        "%PYTHON%" -m pip install --no-index --find-links "%~dp0bin\windows-x86_64\python\wheels" -r "%~dp0app\requirements.txt"
    ) else (
        echo [!] Dépendances Python manquantes et wheels absents.
        pause
        exit /b 1
    )
)

:: Ouverture navigateur (3 s)
start "" /b cmd /c "timeout /t 3 /nobreak >nul & start http://127.0.0.1:!PORT!"

:: Lancement uvicorn
"%PYTHON%" -m uvicorn app.main:app --host !BIND_HOST! --port !PORT! --log-level info --no-access-log

endlocal
