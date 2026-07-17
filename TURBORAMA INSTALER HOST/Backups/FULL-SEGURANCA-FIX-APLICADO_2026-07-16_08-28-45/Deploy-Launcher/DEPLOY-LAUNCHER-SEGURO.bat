@echo off
chcp 65001 >nul
title TurboRama - Deploy Launcher SEGURO
net session >nul 2>&1
if errorlevel 1 (
  echo A pedir Administrador...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "SRC=D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\Projeto Novo TurboRama"
set "OUT=C:\TurboRama\App\Launcher"
set "PROJ=%SRC%\src\TurboRama.Launcher\TurboRama.Launcher.csproj"
set "WDPROJ=%SRC%\src\TurboRama.Watchdog\TurboRama.Watchdog.csproj"
set "WDOUT=C:\TurboRama\App\Watchdog"

echo ============================================
echo  DEPLOY SEGURO - kill -^> publish -^> restart
echo ============================================
echo.

echo [1/5] A terminar Launcher + parar Watchdog service...
taskkill /F /IM TurboRama.Launcher.exe >nul 2>nul
sc stop TurboRamaWatchdog >nul 2>nul
timeout /t 2 /nobreak >nul
taskkill /F /IM TurboRama.Watchdog.exe >nul 2>nul
timeout /t 1 /nobreak >nul
echo OK

echo [2/5] Publish Launcher Release...
dotnet publish "%PROJ%" -c Release -o "%OUT%" --nologo
if errorlevel 1 (
  echo FALHA publish Launcher
  pause
  exit /b 1
)
echo OK

echo [3/5] Publish Watchdog Release + start service...
if exist "%WDPROJ%" (
  if not exist "%WDOUT%" mkdir "%WDOUT%"
  dotnet publish "%WDPROJ%" -c Release -o "%WDOUT%" --nologo
  sc start TurboRamaWatchdog >nul 2>nul
  echo Watchdog publish done
) else (
  echo Watchdog project skip
)

echo [4/5] Registar agent + keep-alive + arrancar...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d "\"%OUT%\TurboRama.Launcher.exe\" --security-agent" /f >nul
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d "\"%OUT%\TurboRama.Launcher.exe\" --security-agent" /f >nul
schtasks /Delete /TN "TurboRamaSecurityAgent" /F >nul 2>nul
schtasks /Create /TN "TurboRamaSecurityAgent" /SC ONLOGON /RL LIMITED /F /TR "\"%OUT%\TurboRama.Launcher.exe\" --security-agent" >nul
schtasks /Delete /TN "TurboRamaSecurityAgentKeepAlive" /F >nul 2>nul
schtasks /Create /TN "TurboRamaSecurityAgentKeepAlive" /SC MINUTE /MO 2 /RL LIMITED /F /TR "\"%OUT%\TurboRama.Launcher.exe\" --security-agent" >nul
start "" "%OUT%\TurboRama.Launcher.exe" --security-agent
timeout /t 2 /nobreak >nul
echo OK

echo [5/5] Heartbeat:
if exist "C:\TurboRama\Logs\security-agent-alive.txt" (
  type "C:\TurboRama\Logs\security-agent-alive.txt"
) else (
  echo  (ainda sem alive - aguarde 15s)
)

echo.
echo PRONTO. Teste: TESTAR-SEGURANCA-COMPLETO.bat  ou  Ctrl+End
echo.
pause
