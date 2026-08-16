@echo off
setlocal EnableExtensions EnableDelayedExpansion
title TurboRama - Encerramento profissional para instalacao

set "INSTALLER=%~dp0INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe"
set "LOG=%~dp0ENCERRAR-TURBORAMA-COM-ADMIN.log"

fltmc.exe >nul 2>&1
if errorlevel 1 (
    echo Solicitando permissao de administrador...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo ============================================================
echo   PREPARANDO O TURBORAMA PARA INSTALACAO
echo ============================================================
echo.

>"%LOG%" echo [%date% %time%] Inicio da preparacao profissional

echo [1/8] Desativando tarefas que reiniciam o sistema...
schtasks.exe /End /TN "\TurboRama PIX Agent" >nul 2>&1
schtasks.exe /Change /TN "\TurboRama PIX Agent" /Disable >nul 2>&1
>>"%LOG%" echo [%date% %time%] Tarefa PIX legada mantida desativada: \TurboRama PIX Agent
for %%T in (
    "\TurboRamaSecurityAgent"
    "\TurboRamaSecurityAgentKeepAlive"
) do (
    schtasks.exe /End /TN "%%~T" >nul 2>&1
    schtasks.exe /Change /TN "%%~T" /Disable >nul 2>&1
    >>"%LOG%" echo [%date% %time%] Tarefa suspensa: %%~T
)

echo [2/8] Desativando temporariamente Watchdog e Maintenance...
for %%S in (TurboRamaWatchdog TurboRamaMaintenance) do (
    sc.exe stop "%%S" >nul 2>&1
    sc.exe config "%%S" start= disabled >nul 2>&1
    >>"%LOG%" echo [%date% %time%] Servico suspenso: %%S
)

echo [3/8] Parando outros servicos relacionados...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$items = Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'TurboRama|EmulationStation' -or $_.DisplayName -match 'TurboRama|EmulationStation' -or $_.PathName -match 'TurboRama|EmulationStation|TurboRamaPixAgent' }; foreach ($item in $items) { if ($item.State -ne 'Stopped') { Stop-Service -Name $item.Name -Force -ErrorAction SilentlyContinue } }"

echo [4/8] Encerrando todas as arvores de processos...
call :CloseProcesses
timeout.exe /t 3 /nobreak >nul
call :CloseProcessesForce
timeout.exe /t 2 /nobreak >nul

echo [5/8] Confirmando que nenhum componente permaneceu aberto...
set "RESTANTES="
for %%P in (
    emulationstation.exe
    TurboRama.Launcher.exe
    TurboRama.exe
    TurboRamaPixAgent.exe
    TurboRama.Watchdog.exe
    TurboRama.Maintenance.exe
    emulatorLauncher.exe
    okemulationstation.exe
    TurboRamaInstaller.exe
    7za.exe
) do (
    tasklist.exe /FI "IMAGENAME eq %%P" /NH 2>nul | find.exe /I "%%P" >nul && set "RESTANTES=1"
)

if defined RESTANTES (
    echo.
    echo ERRO: algum processo ainda esta bloqueando a instalacao.
    echo As tarefas de seguranca serao reativadas. Reinicie o Windows e tente novamente.
    >>"%LOG%" echo [%date% %time%] ERRO: processos restantes detectados
    tasklist.exe >>"%LOG%" 2>&1
    call :EnableTasks
    pause
    exit /b 1
)

if not exist "%INSTALLER%" (
    echo.
    echo ERRO: o instalador nao foi encontrado na mesma pasta deste arquivo.
    echo As tarefas de seguranca serao reativadas.
    call :EnableTasks
    pause
    exit /b 2
)

>>"%LOG%" echo [%date% %time%] Verificacao aprovada: nenhum processo restante
echo [6/8] Removendo somente stagings antigos comprovadamente vazios...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$root = [IO.Path]::GetFullPath($env:ProgramData); Get-ChildItem -LiteralPath $root -Directory -Force -Filter 'TurboRamaInstaller-stage-*' -ErrorAction SilentlyContinue | ForEach-Object { $path = [IO.Path]::GetFullPath($_.FullName); if ($path.StartsWith($root + '\TurboRamaInstaller-stage-', [StringComparison]::OrdinalIgnoreCase) -and @(Get-ChildItem -LiteralPath $path -Force -ErrorAction Stop).Count -eq 0) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue } }"
>>"%LOG%" echo [%date% %time%] Limpeza restrita de stagings vazios concluida

echo [7/8] Tudo encerrado. Abrindo o instalador...
echo Nao feche esta janela durante a instalacao.
echo.
start "" /wait "%INSTALLER%"
set "INSTALL_RESULT=%ERRORLEVEL%"

echo.
echo [8/8] Restaurando seguranca e servicos do TurboRama...
call :EnableTasks

echo.
if not "%INSTALL_RESULT%"=="0" (
    echo O instalador terminou com o codigo %INSTALL_RESULT%.
    echo A seguranca foi reativada, mas a instalacao precisa ser verificada.
    pause
    exit /b %INSTALL_RESULT%
)

echo Instalacao encerrada e seguranca reativada com sucesso.
>>"%LOG%" echo [%date% %time%] Instalador finalizado com codigo %INSTALL_RESULT%
pause
exit /b 0

:CloseProcesses
for %%P in (
    emulationstation.exe
    TurboRama.Launcher.exe
    TurboRama.exe
    TurboRamaPixAgent.exe
    TurboRama.Watchdog.exe
    TurboRama.Maintenance.exe
    emulatorLauncher.exe
    okemulationstation.exe
    TurboRamaPixOwnerConfigurator.exe
    TurboRamaPixCredentialEditor.exe
    CONFIGURAR-USER-TOKEN-PIX.exe
    CONFIGURAR-ACCESS-TOKEN-PIX.exe
    TurboRamaInstaller.exe
    INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe
    7za.exe
) do taskkill.exe /IM "%%P" /T >nul 2>&1

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$items = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'TurboRamaPixAgent\.dll' }; foreach ($item in $items) { Invoke-CimMethod -InputObject $item -MethodName Terminate | Out-Null }"
exit /b 0

:CloseProcessesForce
for %%P in (
    emulationstation.exe
    TurboRama.Launcher.exe
    TurboRama.exe
    TurboRamaPixAgent.exe
    TurboRama.Watchdog.exe
    TurboRama.Maintenance.exe
    emulatorLauncher.exe
    okemulationstation.exe
    TurboRamaPixOwnerConfigurator.exe
    TurboRamaPixCredentialEditor.exe
    CONFIGURAR-USER-TOKEN-PIX.exe
    CONFIGURAR-ACCESS-TOKEN-PIX.exe
    TurboRamaInstaller.exe
    INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe
    7za.exe
) do taskkill.exe /F /IM "%%P" /T >nul 2>&1
exit /b 0

:EnableTasks
schtasks.exe /End /TN "\TurboRama PIX Agent" >nul 2>&1
schtasks.exe /Change /TN "\TurboRama PIX Agent" /Disable >nul 2>&1
for %%T in (
    "\TurboRamaSecurityAgent"
    "\TurboRamaSecurityAgentKeepAlive"
) do schtasks.exe /Change /TN "%%~T" /Enable >nul 2>&1
for %%S in (TurboRamaWatchdog TurboRamaMaintenance) do sc.exe config "%%S" start= auto >nul 2>&1
>>"%LOG%" echo [%date% %time%] Seguranca e servicos restaurados; tarefa PIX legada permanece desativada
exit /b 0
