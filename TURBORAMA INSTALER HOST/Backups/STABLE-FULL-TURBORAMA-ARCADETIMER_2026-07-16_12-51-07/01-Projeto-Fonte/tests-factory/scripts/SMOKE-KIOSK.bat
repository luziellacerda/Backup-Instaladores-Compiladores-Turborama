@echo off
chcp 65001 >nul
title SMOKE Kiosk TurboRama
set "SMOKE=D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\tests\lab\SMOKE-KIOSK.bat"
if not exist "%SMOKE%" (
  echo FALHA: smoke nao encontrado:
  echo %SMOKE%
  pause
  exit /b 1
)
call "%SMOKE%"
exit /b %ERRORLEVEL%
