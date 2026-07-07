@echo off
title Turborama - Compilar Tudo Automaticamente
color 0A
setlocal

echo.
echo ================================================
echo   TURBORAMA - COMPILACAO AUTOMATICA COMPLETA
echo ================================================
echo.
echo Este script vai:
echo   1. Fechar processos que travam arquivos
echo   2. Limpar bin/obj e compilacoes antigas
echo   3. Compilar TurboRama.exe
echo   4. Compilar InstallerHost.exe
echo   5. Compilar RetroBuild.exe
echo   6. Atualizar TurboRama.7z
echo   7. Copiar InstallerHost.exe para pasta do RetroBuild
echo.
echo Feche o Visual Studio antes de continuar.
echo.

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"

if not exist "%ROOT%\Compilar_Tudo_Turborama.ps1" (
    echo ERRO: Compilar_Tudo_Turborama.ps1 nao encontrado em:
    echo %ROOT%
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\Compilar_Tudo_Turborama.ps1" -Configuration Release

endlocal