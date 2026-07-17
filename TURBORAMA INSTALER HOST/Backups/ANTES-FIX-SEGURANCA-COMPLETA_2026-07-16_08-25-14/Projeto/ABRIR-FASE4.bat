@echo off
chcp 65001 >nul
title TurboRama Fase 4
set "OUT=D:\tr-phase4-ui"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"

echo Compilando Fase 4...
"%DOTNET%" build "%~dp0src\TurboRama.Watchdog\TurboRama.Watchdog.csproj" -c Release /p:OutputPath=%OUT%\ /p:AppendTargetFrameworkToOutputPath=false
if errorlevel 1 goto fail
"%DOTNET%" build "%~dp0src\TurboRama.Maintenance\TurboRama.Maintenance.csproj" -c Release /p:OutputPath=%OUT%\ /p:AppendTargetFrameworkToOutputPath=false
if errorlevel 1 goto fail
"%DOTNET%" build "%~dp0src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release /p:OutputPath=%OUT%\ /p:AppendTargetFrameworkToOutputPath=false
if errorlevel 1 goto fail
"%DOTNET%" build "%~dp0src\TurboRama.UI\TurboRama.UI.csproj" -c Release /p:OutputPath=%OUT%\ /p:AppendTargetFrameworkToOutputPath=false
if errorlevel 1 goto fail

echo Abrindo UI Fase 4 (Admin)...
powershell -Command "Start-Process -FilePath '%OUT%\TurboRama.UI.exe' -Verb RunAs"
exit /b 0

:fail
echo FALHA.
pause
exit /b 1
