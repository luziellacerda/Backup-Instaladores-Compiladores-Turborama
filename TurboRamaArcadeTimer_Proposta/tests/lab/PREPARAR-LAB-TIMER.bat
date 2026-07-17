@echo off
chcp 65001 >nul
title Preparar lab Timer
set ROOT=%~dp0..\..
set PROJ=%ROOT%\src\TurboRama.ArcadeTimer\TurboRama.ArcadeTimer.csproj
set OUT=%ROOT%\tests\lab\bin-smoke

echo Compilar + copiar config.lab.json ...
dotnet publish "%PROJ%" -c Release -o "%OUT%" --nologo
if errorlevel 1 (
  echo FALHA publish
  pause
  exit /b 1
)
copy /Y "%ROOT%\tests\configs\config.lab.json" "%OUT%\config.json" >nul
echo.
echo OK. Pasta:
echo   %OUT%
echo.
echo 1. Corra SMOKE-KIOSK.bat
echo 2. Inicie TurboRama.ArcadeTimer.exe nesta pasta
echo 3. F10 = ficha de teste
echo 4. Notepad = emulador falso (lista lab)
echo 5. Preencha checklists A depois B
echo.
start "" explorer "%OUT%"
pause
