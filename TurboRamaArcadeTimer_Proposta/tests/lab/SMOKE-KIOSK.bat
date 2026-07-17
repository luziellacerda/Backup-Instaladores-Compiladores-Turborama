@echo off
chcp 65001 >nul
title SMOKE Kiosk TurboRama (seguro)
set FAIL=0
set OUT=%~dp0..\results\smoke-kiosk-%DATE:~6,4%%DATE:~3,2%%DATE:~0,2%-%TIME:~0,2%%TIME:~3,2%.txt
set OUT=%OUT: =0%

echo === SMOKE KIOSK TURBORAMA === > "%OUT%"
echo %DATE% %TIME% >> "%OUT%"
echo. >> "%OUT%"

echo [1] MsKeyboardFilter...
sc query MsKeyboardFilter | findstr /I "RUNNING" >nul
if errorlevel 1 (echo FAIL filter & echo FAIL filter >> "%OUT%" & set FAIL=1) else (echo OK filter & echo OK filter >> "%OUT%")

echo [2] CAD Blocked...
reg query "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Alt+Del" 2>nul | findstr /I "Blocked" >nul
if errorlevel 1 (echo FAIL CAD & echo FAIL CAD >> "%OUT%" & set FAIL=1) else (echo OK CAD & echo OK CAD >> "%OUT%")

echo [3] Agent alive...
if exist "C:\TurboRama\Logs\security-agent-alive.txt" (
  echo OK agent
  type "C:\TurboRama\Logs\security-agent-alive.txt"
  type "C:\TurboRama\Logs\security-agent-alive.txt" >> "%OUT%"
) else (
  echo FAIL agent
  echo FAIL agent >> "%OUT%"
  set FAIL=1
)

echo [4] Launcher deploy...
if exist "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" (
  echo OK launcher
  echo OK launcher >> "%OUT%"
) else (
  echo FAIL launcher
  echo FAIL launcher >> "%OUT%"
  set FAIL=1
)

echo [5] F10 free on Keyboard Filter? (should not be Blocked)
reg query "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "F10" 2>nul | findstr /I "Blocked" >nul
if errorlevel 1 (
  echo OK F10 not blocked in filter
  echo OK F10 not blocked >> "%OUT%"
) else (
  echo WARN F10 is Blocked - coin key may fail
  echo WARN F10 Blocked >> "%OUT%"
)

echo [6] Disk C free...
powershell -NoProfile -Command "$g=(Get-PSDrive C).Free/1GB; if($g -lt 5){exit 1}else{exit 0}"
if errorlevel 1 (echo WARN disk low & echo WARN disk >> "%OUT%") else (echo OK disk & echo OK disk >> "%OUT%")

echo.
echo Relatorio: %OUT%
if %FAIL% equ 0 (echo RESULTADO: PASS & echo RESULTADO: PASS >> "%OUT%") else (echo RESULTADO: FAIL & echo RESULTADO: FAIL >> "%OUT%")
echo.
pause
exit /b %FAIL%
