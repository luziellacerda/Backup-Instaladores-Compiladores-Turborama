@echo off
chcp 65001 >nul
title TurboRama Secure - Installer
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting Administrator...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo ============================================
echo   TurboRama Secure - Installer UI
echo ============================================
echo Flow: Preflight - Phase2 Kiosk - Phase3 Services - Reboot
echo Phase4 optional only if you accept the risk.
echo.
if not exist "%~dp0Installer\TurboRama.UI.exe" (
  echo ERROR: Installer\TurboRama.UI.exe missing
  pause
  exit /b 1
)
call "%~dp0scripts-internal\SEED-APP.bat"
start "" "%~dp0Installer\TurboRama.UI.exe"
exit /b 0