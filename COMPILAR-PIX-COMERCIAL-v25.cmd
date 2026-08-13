@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0COMPILAR-PIX-COMERCIAL-v25.ps1" -ProtecaoComercial -ExigirAssinatura -TestarInstalador %*
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%
