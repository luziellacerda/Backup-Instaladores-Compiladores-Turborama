@echo off
chcp 65001 >nul
title TurboRama - Phase 6 Factory Accept
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set "UI=%~dp0Installer\TurboRama.UI.exe"
set "RESULT=%TEMP%\turborama-phase6.txt"
if not exist "%UI%" (
  echo ERROR: UI missing
  pause
  exit /b 1
)
echo Phase 6 validate + clear-locks...
"%UI%" --validate --clear-locks --quiet --result "%RESULT%"
set ERR=%ERRORLEVEL%
type "%RESULT%" 2>nul
echo.
if %ERR% equ 0 (echo ACCEPT OK) else (echo ACCEPT FAILED)
pause
exit /b %ERR%