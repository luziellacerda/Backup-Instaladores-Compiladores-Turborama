@echo off
set "TR_STATE=C:\TurboRama\State"
if not exist "%TR_STATE%" mkdir "%TR_STATE%" 2>nul
> "%TR_STATE%\power-request.txt" echo reboot
shutdown /a >nul 2>&1
taskkill /IM emulationstation.exe /F >nul 2>&1
taskkill /IM TurboRama.exe /F >nul 2>&1
exit /b 0
