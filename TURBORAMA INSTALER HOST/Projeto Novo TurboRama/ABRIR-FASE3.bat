@echo off
chcp 65001 >nul
title TurboRama Fase 3 - fix 1053
set "OUT=D:\tr-phase3-ui"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"

echo [1/4] Publish Watchdog...
"%DOTNET%" publish "%~dp0src\TurboRama.Watchdog\TurboRama.Watchdog.csproj" -c Release -r win-x64 --self-contained false -o "%OUT%\watchdog"
if errorlevel 1 goto fail

echo [2/4] Publish Maintenance...
"%DOTNET%" publish "%~dp0src\TurboRama.Maintenance\TurboRama.Maintenance.csproj" -c Release -r win-x64 --self-contained false -o "%OUT%\maintenance"
if errorlevel 1 goto fail

echo [3/4] Publish Launcher + UI...
"%DOTNET%" publish "%~dp0src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 goto fail
"%DOTNET%" publish "%~dp0src\TurboRama.UI\TurboRama.UI.csproj" -c Release -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 goto fail

REM espelha services na pasta do UI para Deploy achar
xcopy /E /Y /I "%OUT%\watchdog\*" "%OUT%\" >nul
xcopy /E /Y /I "%OUT%\maintenance\*" "%OUT%\" >nul

echo [4/4] Abrindo UI Admin...
powershell -Command "Start-Process -FilePath '%OUT%\TurboRama.UI.exe' -Verb RunAs"
exit /b 0

:fail
echo FALHA na publicacao.
pause
exit /b 1
