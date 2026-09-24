@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: Rôle    : launcher AfricAIsoft Portable Studio pour Windows (double-clic).
::           1. vérifie que la clé est inscriptible ;
::           2. résout bin\windows\ (nommage verrouillé ; repli sur l'ancien
::              bin\windows-x86_64\) et le backend (CUDA > Vulkan > CPU, repli
::              CPU si le binaire du backend détecté est absent) ;
::           3. vérifie le runtime VC++ (système OU livré avec llama-server) ;
::           4. prépare le Python embarqué au 1er lancement, hors ligne et
::              sans pip (scripts\install-wheels.py) ;
::           5. si le studio tourne déjà : ouvre simplement le navigateur ;
::           6. démarre le studio et ouvre le navigateur DÈS qu'il répond
::              (le chargement du modèle peut prendre 1 à 2 minutes).
:: Auteur  : AfricAIsoft
:: Licence : MIT
:: Date    : 2026-08-24
:: Version : 0.6.1 (2026-09-24) — chemins bin\windows\, dépendances sans pip,
::           ouverture du navigateur sur disponibilité réelle, messages guidés.
:: Note    : fichier en CRLF obligatoire (voir .gitattributes).
:: ─────────────────────────────────────────────────────────────────────────────
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul
title AfricAIsoft Portable Studio
cd /d "%~dp0"
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo.
echo ============================================================
echo   AfricAIsoft Portable Studio - Windows
echo ============================================================
echo.

:: ── [1] Clé inscriptible + dossiers runtime ────────────────────────────────
if not exist "%ROOT%\logs\" mkdir "%ROOT%\logs" 2>nul
if not exist "%ROOT%\data\pids\" mkdir "%ROOT%\data\pids" 2>nul
break > "%ROOT%\logs\.write-test" 2>nul
if not exist "%ROOT%\logs\.write-test" goto :err_readonly
del "%ROOT%\logs\.write-test" >nul 2>&1

:: ── [2] Plateforme : bin\windows\ ; repli sur l'ancien bin\windows-x86_64\ ─
set "PLAT=windows"
if not exist "%ROOT%\bin\windows\" if exist "%ROOT%\bin\windows-x86_64\" set "PLAT=windows-x86_64"
if not exist "%ROOT%\bin\%PLAT%\" goto :err_noplatform

:: ── [3] Backend GPU (CUDA > Vulkan > CPU) ──────────────────────────────────
set "BACKEND=cpu"
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\scripts\detect-backend.ps1" > "%ROOT%\logs\backend.log" 2>&1
for /f "usebackq tokens=1,* delims==" %%A in ("%ROOT%\logs\backend.log") do (
    if /i "%%A"=="BACKEND" set "BACKEND=%%B"
)
if not exist "%ROOT%\bin\%PLAT%\!BACKEND!\llama-server.exe" set "BACKEND=cpu"
set "BIN_DIR=%ROOT%\bin\%PLAT%\!BACKEND!"
if not exist "!BIN_DIR!\llama-server.exe" goto :err_nobinary
echo [+] Backend : !BACKEND!

:: ── [4] Runtime Visual C++ (système, ou livré à côté de llama-server) ──────
set "VC_OK=1"
for %%D in (vcruntime140.dll vcruntime140_1.dll msvcp140.dll) do (
    if not exist "%SystemRoot%\System32\%%D" if not exist "!BIN_DIR!\%%D" set "VC_OK=0"
)
if "!VC_OK!"=="0" goto :err_vcredist

:: ── [5] Python : embarqué, sinon Python système (hors alias Microsoft Store) ─
set "PYDIR=%ROOT%\bin\%PLAT%\python"
set "PYTHON=%PYDIR%\python.exe"
set "PORTABLE_PY=1"
if exist "%PYTHON%" goto :python_ok
set "PORTABLE_PY=0"
set "PYTHON="
for /f "delims=" %%i in ('where python 2^>nul ^| findstr /v /i "WindowsApps"') do (
    if not defined PYTHON set "PYTHON=%%i"
)
if not defined PYTHON goto :err_python
echo [i] Python embarqué absent : utilisation de !PYTHON!
:python_ok

:: ── [6] Hôte et port (config\settings.json, valeurs par défaut sinon) ──────
if not exist "%ROOT%\config\settings.json" if exist "%ROOT%\config\settings.example.json" copy /y "%ROOT%\config\settings.example.json" "%ROOT%\config\settings.json" >nul
set "BIND_HOST=127.0.0.1"
set "PORT=8080"
for /f "usebackq tokens=1,2" %%a in (`call "%PYTHON%" -c "import json;s=json.load(open(r'config/settings.json',encoding='utf-8-sig')).get('server',{});print(s.get('bind_host','127.0.0.1'),s.get('port',8080))" 2^>nul`) do (
    set "BIND_HOST=%%a"
    set "PORT=%%b"
)
echo !PORT!| findstr /r /x "[0-9][0-9]*" >nul || set "PORT=8080"
set "BROWSE_HOST=!BIND_HOST!"
if "!BIND_HOST!"=="0.0.0.0" set "BROWSE_HOST=127.0.0.1"
if "!BIND_HOST!"=="::" set "BROWSE_HOST=127.0.0.1"
if /i "!BIND_HOST!"=="localhost" set "BROWSE_HOST=127.0.0.1"
set "URL=http://!BROWSE_HOST!:!PORT!"

:: ── [7] Studio déjà lancé ? (10 = oui, 11 = port pris par autre chose) ─────
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 4 -Uri '!URL!/health'; if ($r.Content -match 'platform') { exit 10 } else { exit 11 } } catch { if ($_.Exception.Response) { exit 11 }; $c = New-Object Net.Sockets.TcpClient; try { $c.Connect('!BROWSE_HOST!', !PORT!); exit 11 } catch { exit 0 } finally { $c.Close() } }" >nul 2>&1
set "PORT_STATE=!ERRORLEVEL!"
if "!PORT_STATE!"=="10" goto :already_running
if "!PORT_STATE!"=="11" goto :err_port

:: ── [8] Dépendances Python (1er lancement : installation hors ligne) ───────
"%PYTHON%" -c "import fastapi,uvicorn,httpx,pydantic" >nul 2>&1
if not errorlevel 1 goto :deps_ok
if not exist "%PYDIR%\wheels\" goto :err_nowheels
echo [+] Premier lancement : installation des dépendances Python hors ligne...
if "!PORTABLE_PY!"=="1" goto :deps_portable
"%PYTHON%" -m pip install --no-index --find-links "%PYDIR%\wheels" -r "%ROOT%\app\requirements.txt"
goto :deps_check
:deps_portable
"%PYTHON%" "%ROOT%\scripts\install-wheels.py" "%PYDIR%" "%PYDIR%\wheels"
:deps_check
"%PYTHON%" -c "import fastapi,uvicorn,httpx,pydantic" >nul 2>&1
if errorlevel 1 goto :err_deps
:deps_ok

:: ── [9] Environnement de l'orchestrateur (inchangé) ─────────────────────────
set "PATH=!BIN_DIR!;%PATH%"
set "STUDIO_ROOT=%~dp0"
set "STUDIO_API_PREFIX="
set "STUDIO_BIND_HOST=!BIND_HOST!"
set "STUDIO_PORT=!PORT!"
set "STUDIO_BIN_DIR_OVERRIDE=!BIN_DIR!"
echo %random% > "%ROOT%\data\pids\orchestrator.pid"

:: ── [10] Navigateur : ouvert dès que le studio répond (15 min max) ─────────
start "" /b powershell -NoProfile -ExecutionPolicy Bypass -Command "for ($i = 0; $i -lt 900; $i++) { $c = New-Object Net.Sockets.TcpClient; try { $c.Connect('!BROWSE_HOST!', !PORT!); $c.Close(); Start-Process '!URL!'; exit 0 } catch { $c.Close(); Start-Sleep -Seconds 1 } }"

echo [+] Adresse : !URL!
echo.
echo     Le navigateur s'ouvrira tout seul dès que le studio sera prêt.
echo     Le chargement du modèle peut prendre 1 à 2 minutes.
echo.
echo     LAISSEZ CETTE FENÊTRE OUVERTE. Fermez-la pour arrêter le studio.
echo.

:: ── [11] Lancement uvicorn ─────────────────────────────────────────────────
"%PYTHON%" -m uvicorn app.main:app --host !BIND_HOST! --port !PORT! --log-level info --no-access-log
set "RC=!ERRORLEVEL!"
if "!RC!"=="0" goto :end
echo.
echo [x] Le studio s'est arrêté de façon inattendue, code !RC!.
echo     Consultez les messages ci-dessus et le dossier logs\.
goto :pause_fail

:: ── Cas particuliers et erreurs guidées ─────────────────────────────────────
:already_running
echo [+] Le studio est déjà lancé : ouverture de !URL!
start "" "!URL!"
timeout /t 3 /nobreak >nul
goto :end

:err_readonly
echo [x] Impossible d'écrire sur la clé : elle est en lecture seule.
echo     Vérifiez l'interrupteur de verrouillage de la clé, puis relancez.
goto :pause_fail

:err_noplatform
echo [x] Cette clé ne contient pas les programmes pour Windows : bin\windows\ absent.
echo     Refaites la clé avec le Key Builder en cochant « Windows x64 ».
goto :pause_fail

:err_nobinary
echo [x] llama-server.exe introuvable dans bin\%PLAT%\cpu\.
echo     La clé est incomplète : refaites-la avec le Key Builder.
goto :pause_fail

:err_vcredist
echo [x] Composant Windows manquant : Microsoft Visual C++ Redistributable.
echo     Installez-le une seule fois, puis relancez le studio.
choice /c ON /n /m "    Ouvrir la page de téléchargement maintenant ? [O/N] "
if not errorlevel 2 start "" "https://aka.ms/vs/17/release/vc_redist.x64.exe"
goto :pause_fail

:err_python
echo [x] Python introuvable : le Python embarqué de la clé est absent
echo     et aucun Python 3.12 n'est installé sur ce PC.
echo     Refaites la clé avec le Key Builder, ou installez Python 3.12.
goto :pause_fail

:err_port
echo [x] Le port !PORT! est déjà utilisé par une autre application.
echo     Fermez-la, ou changez « port » dans config\settings.json.
goto :pause_fail

:err_nowheels
echo [x] Dépendances Python absentes et aucun paquet embarqué
echo     dans bin\%PLAT%\python\wheels\. Refaites la clé avec le Key Builder.
goto :pause_fail

:err_deps
echo [x] Les dépendances Python n'ont pas pu être installées.
echo     Consultez les messages ci-dessus, puis refaites la clé si besoin.
goto :pause_fail

:pause_fail
echo.
pause
endlocal
exit /b 1

:end
endlocal
exit /b 0
