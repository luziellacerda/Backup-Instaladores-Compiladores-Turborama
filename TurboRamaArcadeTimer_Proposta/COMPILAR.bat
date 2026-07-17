@echo off
setlocal
cd /d "%~dp0src\TurboRama.ArcadeTimer"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERRO: .NET 8 SDK nao encontrado.
    echo Instale o .NET 8 SDK e execute novamente.
    pause
    exit /b 1
)

dotnet restore
if errorlevel 1 goto erro

dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true

if errorlevel 1 goto erro

echo.
echo COMPILACAO CONCLUIDA.
echo Saida:
echo src\TurboRama.ArcadeTimer\bin\Release\net8.0-windows\win-x64\publish
pause
exit /b 0

:erro
echo.
echo A COMPILACAO FALHOU.
pause
exit /b 1
