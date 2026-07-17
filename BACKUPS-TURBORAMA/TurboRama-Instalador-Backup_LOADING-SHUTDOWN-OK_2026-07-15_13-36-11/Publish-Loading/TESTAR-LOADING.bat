@echo off
chcp 65001 >nul
title TurboRama - Testar Loading (sem reiniciar)
echo.
echo  ========================================
echo   TURBORAMA - Preview da tela de loading
echo   Nao precisa reiniciar o PC
echo  ========================================
echo.

set "EXE=C:\TurboRama\App\Launcher\TurboRama.Launcher.exe"
if not exist "%EXE%" (
  set "EXE=%~dp0src\TurboRama.Launcher\bin\Release\net8.0-windows\win-x64\TurboRama.Launcher.exe"
)
if not exist "%EXE%" (
  echo EXE nao encontrado. Compile o Launcher primeiro.
  pause
  exit /b 1
)

echo A executar: "%EXE%" --test-loading
echo Feche a tela ou aguarde o fim do load.
echo.
"%EXE%" --test-loading %*
echo.
echo Preview terminou.
pause
