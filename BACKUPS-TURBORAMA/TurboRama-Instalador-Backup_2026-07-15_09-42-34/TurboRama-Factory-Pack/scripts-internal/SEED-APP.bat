@echo off
set "ROOT=C:\TurboRama"
set "SRC=%~dp0.."
mkdir "%ROOT%\App\Launcher" 2>nul
mkdir "%ROOT%\App\Watchdog" 2>nul
mkdir "%ROOT%\App\Maintenance" 2>nul
mkdir "%ROOT%\App\Tools" 2>nul
mkdir "%ROOT%\Frontend" 2>nul
mkdir "%ROOT%\Config" 2>nul
mkdir "%ROOT%\Logs\Installer" 2>nul
mkdir "%ROOT%\State" 2>nul
mkdir "%ROOT%\Backup" 2>nul
xcopy "%SRC%\App\Launcher\*" "%ROOT%\App\Launcher\" /E /Y /Q >nul
xcopy "%SRC%\App\Watchdog\*" "%ROOT%\App\Watchdog\" /E /Y /Q >nul
xcopy "%SRC%\App\Maintenance\*" "%ROOT%\App\Maintenance\" /E /Y /Q >nul
if exist "%SRC%\App\Tools\Autologon64.exe" copy /Y "%SRC%\App\Tools\Autologon64.exe" "%ROOT%\App\Tools\" >nul
if exist "%SRC%\Config\turborama.json" if not exist "%ROOT%\Config\turborama.json" copy /Y "%SRC%\Config\turborama.json" "%ROOT%\Config\" >nul
if exist "%SRC%\Frontend\*.exe" xcopy "%SRC%\Frontend\*.exe" "%ROOT%\Frontend\" /Y /Q >nul
exit /b 0