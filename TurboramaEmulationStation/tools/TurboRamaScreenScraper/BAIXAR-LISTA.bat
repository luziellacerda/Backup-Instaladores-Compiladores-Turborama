@echo off
title TurboRama - Baixar lista ScreenScraper
cd /d "%~dp0"
if not exist config.json (
  echo.
  echo  FALTA config.json
  echo  1^) Copie config.example.json para config.json
  echo  2^) Preencha: ssid, sspassword, devid, devpassword
  echo  3^) Conta: https://www.screenscraper.fr/membreinscription.php
  echo  4^) DevID: forum developers do ScreenScraper
  echo.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Baixar-Lista.ps1"
echo.
pause
