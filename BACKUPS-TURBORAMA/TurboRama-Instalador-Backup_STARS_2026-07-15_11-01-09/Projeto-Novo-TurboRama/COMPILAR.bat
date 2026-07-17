@echo off
chcp 65001 >nul
title TurboRama - Compilar Projeto Novo
cd /d "%~dp0"

echo ============================================
echo   Projeto Novo TurboRama - Compilar
echo ============================================
echo.

set "DOTNET="
if exist "C:\Program Files\dotnet\dotnet.exe" set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"

if "%DOTNET%"=="" (
  echo ERRO: .NET SDK nao encontrado.
  echo Instale: https://dotnet.microsoft.com/download/dotnet/8.0
  echo Escolha: ".NET 8 SDK"  ^(nao so Runtime^)
  echo.
  pause
  exit /b 1
)

echo Usando: %DOTNET%
"%DOTNET%" --version
echo.

echo [1/3] Restore...
"%DOTNET%" restore "TurboRama.sln"
if errorlevel 1 goto fail

echo.
echo [2/3] Build Release...
"%DOTNET%" build "TurboRama.sln" -c Release --no-restore
if errorlevel 1 goto fail

echo.
echo [3/3] Testes...
"%DOTNET%" test "TurboRama.sln" -c Release --no-build
if errorlevel 1 goto fail

echo.
echo ============================================
echo   OK - Compilacao concluida
echo ============================================
echo.
echo Instalador UI:
echo   %~dp0src\TurboRama.UI\bin\Release\net8.0-windows\TurboRama.UI.exe
echo.
echo Se der erro de arquivo em uso: feche TurboRama.UI e compile de novo.
echo Alternativa Fase 1: ABRIR-FASE1.bat  ^(D:\tr-phase1-ui\TurboRama.UI.exe^)
echo.
echo Para ABRIR a UI ^(como Admin^):
echo   clique com botao direito no EXE - Executar como administrador
echo.
pause
exit /b 0


:fail
echo.
echo ============================================
echo   FALHA na compilacao
echo ============================================
echo.
pause
exit /b 1
