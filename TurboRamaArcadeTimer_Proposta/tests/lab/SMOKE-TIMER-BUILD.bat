@echo off
chcp 65001 >nul
title SMOKE Build Arcade Timer
set ROOT=%~dp0..\..
set PROJ=%ROOT%\src\TurboRama.ArcadeTimer\TurboRama.ArcadeTimer.csproj
set OUT=%ROOT%\tests\lab\bin-smoke
set FAIL=0

echo === SMOKE BUILD TIMER ===
if not exist "%PROJ%" (
  echo FAIL: project not found
  pause
  exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo FAIL: dotnet not in PATH
  pause
  exit /b 1
)

echo [1] Restore + build...
dotnet build "%PROJ%" -c Release -o "%OUT%" --nologo
if errorlevel 1 (
  echo FAIL build
  set FAIL=1
  goto end
)
echo OK build

echo [2] Copy lab config...
copy /Y "%ROOT%\tests\configs\config.lab.json" "%OUT%\config.json" >nul
if errorlevel 1 (echo FAIL config & set FAIL=1) else (echo OK config)

echo [3] EXE exists...
if exist "%OUT%\TurboRama.ArcadeTimer.exe" (echo OK exe) else (echo FAIL exe & set FAIL=1)

echo.
echo Pasta smoke: %OUT%
echo Para teste manual: executar TurboRama.ArcadeTimer.exe e premir F10
echo.

:end
if %FAIL% equ 0 (echo RESULTADO: PASS) else (echo RESULTADO: FAIL)
pause
exit /b %FAIL%
