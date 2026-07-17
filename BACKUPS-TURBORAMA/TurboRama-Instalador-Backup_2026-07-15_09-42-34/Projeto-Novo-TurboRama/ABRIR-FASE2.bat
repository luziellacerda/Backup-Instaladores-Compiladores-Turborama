@echo off
chcp 65001 >nul
title TurboRama Fase 2
REM Fecha UIs antigas se possivel e compila UI+Launcher em D:\tr-phase2-ui

set "OUT=D:\tr-phase2-ui"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"

echo Compilando Fase 2 (UI + Launcher)...
"%DOTNET%" build "%~dp0src\TurboRama.Launcher\TurboRama.Launcher.csproj" -c Release /p:OutputPath=%OUT%\ /p:AppendTargetFrameworkToOutputPath=false
if errorlevel 1 goto fail
"%DOTNET%" build "%~dp0src\TurboRama.UI\TurboRama.UI.csproj" -c Release /p:OutputPath=%OUT%\ /p:AppendTargetFrameworkToOutputPath=false
if errorlevel 1 goto fail

if not exist "%OUT%\TurboRama.UI.exe" (
  echo ERRO: UI nao gerada. Feche TurboRama.UI e tente de novo.
  pause
  exit /b 1
)

echo.
echo Abrindo UI Fase 2 como Admin...
powershell -Command "Start-Process -FilePath '%OUT%\TurboRama.UI.exe' -Verb RunAs"
exit /b 0

:fail
echo FALHA na compilacao.
pause
exit /b 1
