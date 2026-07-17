@echo off
chcp 65001 >nul
title TurboRama - Gerar Pack Fábrica (Fase 5)
cd /d "%~dp0"

set "DOTNET="
if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"
if exist "C:\Program Files\dotnet\dotnet.exe" set "DOTNET=C:\Program Files\dotnet\dotnet.exe"

echo ============================================
echo   Fase 5 — Gerar TurboRama-Factory-Pack
echo ============================================
echo.

if "%DOTNET%"=="" (
  echo ERRO: .NET SDK nao encontrado.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-FactoryPack.ps1" -Dotnet "%DOTNET%" -OutputRoot "D:\tr-factory-pack"
set ERR=%ERRORLEVEL%
echo.
if %ERR% neq 0 (
  echo FALHA ao gerar pack.
  pause
  exit /b %ERR%
)

echo.
echo Abrir pasta do pack?
explorer "D:\tr-factory-pack\TurboRama-Factory-Pack"
pause
exit /b 0
