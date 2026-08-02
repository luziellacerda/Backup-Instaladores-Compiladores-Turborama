@echo off
setlocal
title TurboRama PIX Comercial v14 - Compilacao completa
color 0A

echo.
echo ============================================================
echo   TURBORAMA PIX COMERCIAL v14 - COMPILADOR DE UM CLIQUE
echo ============================================================
echo.
echo Este programa compila o EmulationStation, o agente PIX,
echo o editor externo do Access Token e o instalador EXE unico.
echo Nenhuma ROM, tema instalado, credito ou credencial sera alterado.
echo.

set "SCRIPT=%~dp0COMPILAR-PIX-COMERCIAL-v14.ps1"
if not exist "%SCRIPT%" (
  echo ERRO: arquivo de compilacao nao encontrado:
  echo %SCRIPT%
  pause
  exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
set "RESULT=%ERRORLEVEL%"

echo.
if "%RESULT%"=="0" (
  echo COMPILACAO FINALIZADA COM SUCESSO.
) else (
  echo A COMPILACAO FALHOU. Consulte o arquivo COMPILACAO-v14.log.
)
echo.
pause
exit /b %RESULT%
