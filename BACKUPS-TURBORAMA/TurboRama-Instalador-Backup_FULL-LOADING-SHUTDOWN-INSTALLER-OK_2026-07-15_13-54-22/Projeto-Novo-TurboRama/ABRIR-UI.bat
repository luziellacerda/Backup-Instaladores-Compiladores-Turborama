@echo off
chcp 65001 >nul
title TurboRama UI
cd /d "%~dp0"

set "EXE=%~dp0src\TurboRama.UI\bin\Release\net8.0-windows\TurboRama.UI.exe"
if not exist "%EXE%" (
  echo EXE ainda nao existe. Compilando primeiro...
  call "%~dp0COMPILAR.bat"
)

if not exist "%EXE%" (
  echo ERRO: nao achou o EXE apos compilar.
  pause
  exit /b 1
)

echo Abrindo UI ^(pede Admin se o manifest exigir^)...
start "" "%EXE%"
exit /b 0
