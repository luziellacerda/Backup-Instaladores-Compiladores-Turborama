@echo off
chcp 65001 >nul
title Reinstall TurboRama services
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
call "%~dp0scripts-internal\SEED-APP.bat"
"%~dp0Installer\TurboRama.UI.exe" --phase3 --quiet --result "%TEMP%\turborama-phase3.txt"
type "%TEMP%\turborama-phase3.txt" 2>nul
sc query TurboRamaWatchdog
sc query TurboRamaMaintenance
pause