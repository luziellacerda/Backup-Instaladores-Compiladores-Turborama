@echo off
title Turborama Cleaner - Limpar Cache antes de Compilar
color 0A
setlocal

echo.
echo ================================================
echo      TURBORAMA CLEANER - LIMPAR ANTES
echo ================================================
echo.
echo Este limpador deve ficar na mesma pasta do:
echo   InstallerHost.sln
echo.
echo Ele limpa:
echo   .vs
echo   bin
echo   obj
echo   marca de internet Zone.Identifier
echo.
echo NAO apaga:
echo   resources
echo   arquivos .cs
echo   arquivos .resx
echo   InstallerHost.sln
echo.

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"

if not exist "%ROOT%\InstallerHost.sln" (
    echo ERRO: Nao encontrei InstallerHost.sln nesta pasta:
    echo %ROOT%
    echo.
    echo Coloque este arquivo .bat dentro da pasta:
    echo TURBORAMA CREATOR
    echo junto do InstallerHost.sln
    echo.
    pause
    exit /b 1
)

echo Pasta detectada:
echo %ROOT%
echo.
echo Feche o Visual Studio antes de continuar.
echo.
pause

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\Limpar_Turborama_Cache.ps1" -ProjectRoot "%ROOT%"

echo.
echo ================================================
echo Limpeza finalizada.
echo Agora abra InstallerHost.sln e compile em:
echo   Release ^| Any CPU
echo ================================================
echo.
pause
endlocal
