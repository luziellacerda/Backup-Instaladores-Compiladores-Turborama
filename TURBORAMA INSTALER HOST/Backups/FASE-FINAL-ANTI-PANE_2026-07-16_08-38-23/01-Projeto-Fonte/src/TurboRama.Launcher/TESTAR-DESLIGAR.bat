@echo off
chcp 65001 >nul
title TurboRama - Testar tela de DESLIGAR (sem desligar o PC)
echo.
echo  ========================================
echo   TURBORAMA - Preview tela de desligar
echo   NAO desliga o computador
echo  ========================================
echo.

set "EXE=C:\TurboRama\App\Launcher\TurboRama.Launcher.exe"
if not exist "%EXE%" (
  set "EXE=%~dp0bin\Release\net8.0-windows\win-x64\TurboRama.Launcher.exe"
)
if not exist "%EXE%" (
  echo EXE nao encontrado. Compile o Launcher primeiro.
  pause
  exit /b 1
)

echo A executar: "%EXE%" --test-shutdown
echo Aguarde a animacao. O PC NAO sera desligado.
echo.
"%EXE%" --test-shutdown %*
echo.
echo Preview terminou.
pause
