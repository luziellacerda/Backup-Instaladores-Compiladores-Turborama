@echo off
chcp 65001 >nul
title TurboRama Secure - Automatic install
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set "UI=%~dp0Installer\TurboRama.UI.exe"
set "RESULT=%TEMP%\turborama-auto-install.txt"
if not exist "%UI%" (
  echo ERROR: UI missing
  pause
  exit /b 1
)
echo [1/4] Seed App...
call "%~dp0scripts-internal\SEED-APP.bat"
echo [2/4] Phase 2 Kiosk...
"%UI%" --phase2 --quiet --result "%RESULT%"
if errorlevel 1 (
  echo FAIL Phase 2
  type "%RESULT%" 2>nul
  pause
  exit /b 1
)
echo [3/4] Phase 3 Services...
"%UI%" --phase3 --quiet --result "%RESULT%"
if errorlevel 1 (
  echo FAIL Phase 3
  type "%RESULT%" 2>nul
  pause
  exit /b 1
)
echo [4/4] OK - reboot for Arcade autologon
type "%RESULT%" 2>nul
pause
exit /b 0