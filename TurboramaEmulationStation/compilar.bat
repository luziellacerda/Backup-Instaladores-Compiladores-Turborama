@echo off
setlocal EnableExtensions

title TurboRama EmulationStation - Compilar

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "PS_SCRIPT=%ROOT%\tools\compilar.ps1"

cd /d "%ROOT%"

if not exist "%PS_SCRIPT%" (
    echo.
    echo [ERRO] Programa de compilacao nao encontrado:
    echo        %PS_SCRIPT%
    echo.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"

endlocal & exit /b %EXIT_CODE%