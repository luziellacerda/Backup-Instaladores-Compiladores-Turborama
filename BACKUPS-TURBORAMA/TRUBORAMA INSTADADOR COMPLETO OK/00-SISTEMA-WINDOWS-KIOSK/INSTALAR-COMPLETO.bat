@echo off
chcp 65001 >nul
title TurboRama Secure - Windows Kiosk + Seguranca (fabrica)
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Solicitando Administrador...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo ============================================
echo   TurboRama - WINDOWS KIOSK DE PRODUCAO
echo   Arcade + autologon + servicos +
echo   Keyboard Filter + SecurityAgent
echo   (igual PC referencia)
echo ============================================
echo   Jogos/TurboRama: COPIAR D:\Turborama
echo   DEPOIS do reboot, quando o Windows estiver OK.
echo ============================================
echo.
if exist "%~dp0CHECAR-DOTNET.bat" call "%~dp0CHECAR-DOTNET.bat"
if errorlevel 1 (
  echo Instale .NET 8 Desktop Runtime e rode de novo.
  pause
  exit /b 1
)
set "SETUP=%~dp0TurboRama.Setup.exe"
if not exist "%SETUP%" set "SETUP=%~dp0Installer\TurboRama.UI.exe"
if not exist "%SETUP%" (
  echo ERRO: TurboRama.Setup.exe / Installer\TurboRama.UI.exe ausente
  pause
  exit /b 1
)
set "RESULT=%TEMP%\turborama-factory-full.txt"
echo Instalando Windows kiosk + seguranca... aguarde.
"%SETUP%" --install-full --result "%RESULT%"
set ERR=%ERRORLEVEL%
echo.
type "%RESULT%" 2>nul
echo.
if %ERR% equ 0 (
  echo OK - Windows pronto.
  echo 1 REINICIE o PC
  echo 2 Depois copie D:\Turborama (jogos) para este PC
  echo 3 Confirme D:\Turborama\TurboRama.exe
) else (
  echo FALHA - veja C:\TurboRama\Logs\Installer\
)
pause
exit /b %ERR%