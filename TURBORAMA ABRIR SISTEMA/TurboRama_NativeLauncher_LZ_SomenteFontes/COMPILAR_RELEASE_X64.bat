@echo off
setlocal
cd /d "%~dp0"

set "SLN=%~dp0TurboRamaNativeLauncher.sln"
set "MSBUILD="

where msbuild.exe >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    for /f "delims=" %%M in ('where msbuild.exe 2^>nul') do (
        set "MSBUILD=%%M"
        goto :build
    )
)

if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
    for /f "usebackq delims=" %%M in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        set "MSBUILD=%%M"
        goto :build
    )
)

echo [ERRO] MSBuild nao encontrado.
echo Abra o "Developer Command Prompt for VS" ou instale o workload "Desktop development with C++".
pause
exit /b 1

:build
echo Usando MSBuild: %MSBUILD%
echo Compilando TurboRama Native Launcher em Release x64...
"%MSBUILD%" "%SLN%" /m /t:Rebuild /p:Configuration=Release /p:Platform=x64
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERRO] Falha na compilacao.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [OK] Compilacao concluida.
echo Arquivo gerado em:
echo %~dp0saida\x64\Release\TurboRama.exe
echo.
echo Coloque esse TurboRama.exe na pasta raiz do pacote.
echo Ele vai chamar automaticamente: .\sistema\TurboRama.exe
pause
