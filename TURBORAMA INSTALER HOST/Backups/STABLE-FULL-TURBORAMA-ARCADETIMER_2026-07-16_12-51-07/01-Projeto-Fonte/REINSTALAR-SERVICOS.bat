@echo off
:: Requer Admin — corrige erro 1053 dos serviços TurboRama
chcp 65001 >nul
net session >nul 2>&1
if errorlevel 1 (
  echo Solicitando Admin...
  powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if exist "D:\tr-dotnet\dotnet.exe" set "DOTNET=D:\tr-dotnet\dotnet.exe"
set "ROOT=%~dp0"
set "PUB=D:\tr-phase3-fix"

echo === Publicando servicos ===
"%DOTNET%" publish "%ROOT%src\TurboRama.Watchdog\TurboRama.Watchdog.csproj" -c Release -r win-x64 --self-contained false -o "%PUB%\watchdog"
if errorlevel 1 goto fail
"%DOTNET%" publish "%ROOT%src\TurboRama.Maintenance\TurboRama.Maintenance.csproj" -c Release -r win-x64 --self-contained false -o "%PUB%\maintenance"
if errorlevel 1 goto fail
"%DOTNET%" publish "%ROOT%src\TurboRama.UI\TurboRama.UI.csproj" -c Release -r win-x64 --self-contained false -o "%PUB%\ui"
if errorlevel 1 goto fail

echo === Copiando para C:\TurboRama\App ===
mkdir "C:\TurboRama\App\Watchdog" 2>nul
mkdir "C:\TurboRama\App\Maintenance" 2>nul
xcopy /E /Y /I /Q "%PUB%\watchdog\*" "C:\TurboRama\App\Watchdog\"
xcopy /E /Y /I /Q "%PUB%\maintenance\*" "C:\TurboRama\App\Maintenance\"

echo === Re-registrando servicos ===
sc stop TurboRamaWatchdog >nul 2>&1
sc stop TurboRamaMaintenance >nul 2>&1
timeout /t 2 /nobreak >nul
sc delete TurboRamaWatchdog >nul 2>&1
sc delete TurboRamaMaintenance >nul 2>&1
timeout /t 2 /nobreak >nul

sc create TurboRamaWatchdog binPath= "C:\TurboRama\App\Watchdog\TurboRama.Watchdog.exe" start= auto DisplayName= "TurboRama Watchdog" obj= LocalSystem
sc create TurboRamaMaintenance binPath= "C:\TurboRama\App\Maintenance\TurboRama.Maintenance.exe" start= auto DisplayName= "TurboRama Maintenance" obj= LocalSystem
sc description TurboRamaWatchdog "TurboRama Secure Watchdog"
sc description TurboRamaMaintenance "TurboRama Secure Maintenance"

echo === Iniciando ===
sc start TurboRamaWatchdog
sc start TurboRamaMaintenance
timeout /t 3 /nobreak >nul

echo.
echo === STATUS ===
sc query TurboRamaWatchdog
sc query TurboRamaMaintenance

echo.
echo Logs: C:\TurboRama\Logs\Watchdog  e  C:\TurboRama\Logs\Maintenance
echo UI nova: %PUB%\ui\TurboRama.UI.exe
echo.
pause
exit /b 0

:fail
echo FALHA no publish.
pause
exit /b 1
