@echo off
chcp 65001 >nul
title TurboRama Fase 1
REM Feche a UI antiga antes de recompilar. Esta versao tem barra de progresso + popup ao terminar.

set "EXE=D:\tr-phase1-ui2\TurboRama.UI.exe"
if not exist "%EXE%" set "EXE=D:\tr-phase1-ui\TurboRama.UI.exe"

if not exist "%EXE%" (
  echo Compilando em D:\tr-phase1-ui2 ...
  set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
  if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"
  "%DOTNET%" build "%~dp0src\TurboRama.UI\TurboRama.UI.csproj" -c Release /p:OutputPath=D:\tr-phase1-ui2\ /p:AppendTargetFrameworkToOutputPath=false
  set "EXE=D:\tr-phase1-ui2\TurboRama.UI.exe"
)

if not exist "%EXE%" (
  echo ERRO: nao gerou o EXE. Feche TurboRama.UI e rode de novo.
  pause
  exit /b 1
)

echo Abrindo: %EXE%
powershell -Command "Start-Process -FilePath '%EXE%' -Verb RunAs"
exit /b 0
