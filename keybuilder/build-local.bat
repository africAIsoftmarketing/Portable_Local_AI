@echo off
REM ==========================================================================
REM  Rôle    : build local Windows du Key Builder (.NET 8 WPF) — reproduit
REM            exactement le workflow GitHub Actions build-key-builder.yml.
REM            Aucune commande réseau sauf `dotnet restore` (NuGet).
REM  Usage   : .\build-local.bat [Version]
REM            Version : optionnelle ; par défaut lue depuis keybuilder\VERSION.
REM  Sortie  : keybuilder\AfricAIsoft-KeyBuilder-vX.Y.Z-win64.zip
REM  Auteur  : AfricAIsoft — Licence : MIT
REM ==========================================================================
setlocal EnableDelayedExpansion

REM --- 0) Se placer dans le dossier keybuilder\ (racine de la solution) -----
pushd "%~dp0" >nul 2>&1

REM --- 1) Vérification prérequis --------------------------------------------
where dotnet >nul 2>&1 || (
    echo [ERREUR] .NET SDK 8 introuvable dans PATH. Installez-le depuis
    echo         https://dotnet.microsoft.com/download puis relancez.
    popd
    exit /b 1
)
where powershell >nul 2>&1 || (
    echo [ERREUR] powershell.exe introuvable dans PATH.
    popd
    exit /b 1
)

REM --- 2) Résolution de la version ------------------------------------------
if not "%~1"=="" (
    set "VERSION=%~1"
) else (
    if not exist "VERSION" (
        echo [ERREUR] keybuilder\VERSION absent et aucun argument fourni.
        popd
        exit /b 1
    )
    for /f "usebackq delims=" %%v in ("VERSION") do (
        if not defined VERSION set "VERSION=%%v"
    )
)
echo [build-local] Version cible : %VERSION%

REM --- 3) Restore + tests Core ----------------------------------------------
set "WPF_PROJ=src\AfricAIsoft.KeyBuilder.Wpf\AfricAIsoft.KeyBuilder.Wpf.csproj"
set "CORE_TESTS=tests\AfricAIsoft.KeyBuilder.Core.Tests\AfricAIsoft.KeyBuilder.Core.Tests.csproj"
set "PUBLISH_DIR=publish"

echo [build-local] Restore des dépendances NuGet...
dotnet restore "%WPF_PROJ%" || (popd & exit /b 1)
dotnet restore "%CORE_TESTS%" || (popd & exit /b 1)

echo [build-local] Tests xUnit Core...
dotnet test "%CORE_TESTS%" -c Release --nologo --logger "console;verbosity=minimal" || (
    echo [ERREUR] Les tests Core ont échoué. Corrigez avant de packager.
    popd
    exit /b 1
)

REM --- 4) Publication self-contained single-file (win-x64) -----------------
echo [build-local] Publication self-contained single-file (win-x64)...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish "%WPF_PROJ%" ^
    -c Release -r win-x64 ^
    --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:EnableCompressionInSingleFile=true ^
    /p:DebugType=embedded ^
    /p:PublishReadyToRun=false ^
    -o "%PUBLISH_DIR%" || (popd & exit /b 1)

REM --- 5) Renommage exe : AfricAIsoft.KeyBuilder.exe -> AfricAIsoft-KeyBuilder.exe
if not exist "%PUBLISH_DIR%\AfricAIsoft.KeyBuilder.exe" (
    echo [ERREUR] Exe attendu introuvable.
    dir "%PUBLISH_DIR%"
    popd
    exit /b 1
)
move /y "%PUBLISH_DIR%\AfricAIsoft.KeyBuilder.exe" "%PUBLISH_DIR%\AfricAIsoft-KeyBuilder.exe" >nul

REM --- 6) Garde-fou taille (< 100 Mo) --------------------------------------
for %%A in ("%PUBLISH_DIR%\AfricAIsoft-KeyBuilder.exe") do set "SIZE_BYTES=%%~zA"
set /a "SIZE_MB=%SIZE_BYTES% / 1048576"
echo [build-local] Taille exe : %SIZE_MB% Mo (%SIZE_BYTES% octets)
if %SIZE_MB% GTR 100 (
    echo [ERREUR] L'exe depasse la limite de 100 Mo. Aborte.
    popd
    exit /b 1
)

REM --- 7) Assemblage du zip via PowerShell Compress-Archive -----------------
set "STAGE=stage"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%"
copy /y "%PUBLISH_DIR%\AfricAIsoft-KeyBuilder.exe" "%STAGE%\" >nul
copy /y "README-KeyBuilder.md" "%STAGE%\" >nul
mkdir "%STAGE%\logs" >nul 2>&1
type nul > "%STAGE%\logs\.gitkeep"
if exist "config\settings.example.json" (
    mkdir "%STAGE%\config" >nul 2>&1
    copy /y "config\settings.example.json" "%STAGE%\config\" >nul
)

set "ZIP_NAME=AfricAIsoft-KeyBuilder-v%VERSION%-win64.zip"
if exist "%ZIP_NAME%" del /q "%ZIP_NAME%"
echo [build-local] Creation de l'archive %ZIP_NAME%...
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP_NAME%' -Force" || (
    popd & exit /b 1
)

REM --- 8) Résumé ------------------------------------------------------------
for %%A in ("%ZIP_NAME%") do set "ZIP_BYTES=%%~zA"
set /a "ZIP_MB=%ZIP_BYTES% / 1048576"
echo.
echo [build-local] OK — %ZIP_NAME% (%ZIP_MB% Mo, %ZIP_BYTES% octets)
echo               Deposez cette archive comme livrable.

popd
endlocal
exit /b 0
