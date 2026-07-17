@echo off
chcp 65001 >nul
title TurboRama - Fase 6 Aceite de Fabrica
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
  echo Solicitando Administrador...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "UI="
if exist "D:\tr-factory-pack\TurboRama-Factory-Pack\Installer\TurboRama.UI.exe" set "UI=D:\tr-factory-pack\TurboRama-Factory-Pack\Installer\TurboRama.UI.exe"
if exist "D:\tr-phase3-fix\ui-phase2c\TurboRama.UI.exe" set "UI=D:\tr-phase3-fix\ui-phase2c\TurboRama.UI.exe"
if exist "D:\tr-factory-pack\_ui-phase6\TurboRama.UI.exe" set "UI=D:\tr-factory-pack\_ui-phase6\TurboRama.UI.exe"
if exist "%~dp0artifacts\Phase6\TurboRama.UI.exe" set "UI=%~dp0artifacts\Phase6\TurboRama.UI.exe"

if "%UI%"=="" (
  echo Publicando UI Fase 6...
  set "DOTNET="
  if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"
  if exist "C:\Program Files\dotnet\dotnet.exe" set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
  if "%DOTNET%"=="" (
    echo ERRO: dotnet nao encontrado e UI ausente.
    pause
    exit /b 1
  )
  "%DOTNET%" publish "%~dp0src\TurboRama.UI\TurboRama.UI.csproj" -c Release -r win-x64 --self-contained false -o "D:\tr-factory-pack\_ui-phase6" /p:UseAppHost=true
  set "UI=D:\tr-factory-pack\_ui-phase6\TurboRama.UI.exe"
)

echo UI: %UI%
echo Executando Fase 6 --validate --clear-locks --quiet ...
set "RESULT=C:\TurboRama\Logs\Installer\phase6-last.txt"
"%UI%" --validate --clear-locks --quiet --result "%RESULT%"
set ERR=%ERRORLEVEL%
echo.
echo === RESULTADO ===
type "%RESULT%" 2>nul
echo.
if %ERR% equ 0 (
  echo ACEITE OK
) else (
  echo ACEITE COM FALHAS - veja logs em C:\TurboRama\Logs\Installer\
)
echo.
pause
exit /b %ERR%
