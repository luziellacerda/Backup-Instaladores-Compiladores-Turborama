@echo off
chcp 65001 >nul
title TurboRama - Menu seguranca (Ctrl+End)
echo.
echo  Ctrl+Alt+Del = desativado/esvaziado no kiosk
echo  Ctrl+End     = MENU TURBORAMA
echo.
echo  Preview do menu (PIN = senha kiosk de fabrica):
"C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" --test-security
echo.
echo  Para Ctrl+End global nesta sessao:
echo  start "" "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" --security-agent
echo.
pause
