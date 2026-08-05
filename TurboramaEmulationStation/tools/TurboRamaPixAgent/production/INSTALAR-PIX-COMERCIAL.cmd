@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALAR-PIX-COMERCIAL.ps1"
set "TURBORAMA_EXIT=%ERRORLEVEL%"
endlocal & exit /b %TURBORAMA_EXIT%
