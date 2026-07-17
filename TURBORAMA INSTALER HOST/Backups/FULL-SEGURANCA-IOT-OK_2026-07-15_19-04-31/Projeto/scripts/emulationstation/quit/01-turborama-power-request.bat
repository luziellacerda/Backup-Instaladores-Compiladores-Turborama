@echo off
REM Menu Desligar/Sair do ES ? pede SHUTDOWN ao Launcher (nao relancar TurboRama)
set "TR_STATE=C:\TurboRama\State"
if not exist "%TR_STATE%" mkdir "%TR_STATE%" 2>nul
> "%TR_STATE%\power-request.txt" echo shutdown
shutdown /a >nul 2>&1
taskkill /IM emulationstation.exe /F >nul 2>&1
taskkill /IM TurboRama.exe /F >nul 2>&1
exit /b 0
