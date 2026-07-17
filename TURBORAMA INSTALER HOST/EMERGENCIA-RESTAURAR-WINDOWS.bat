@echo off
cd /d "%~dp0"
title EMERGENCIA - Restaurar Windows TurboRama
color 0C
echo.
echo  ================================================
echo   EMERGENCIA - RESTAURAR WINDOWS NORMAL
echo  ================================================
echo.

net session >nul 2>&1
if errorlevel 1 (
    echo  Elevando para Administrador...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo [1/5] Desativando UWF...
if exist "%WINDIR%\System32\uwfmgr.exe" "%WINDIR%\System32\uwfmgr.exe" filter disable

echo [2/5] Desativando Premium + politicas...
if exist "C:\TurboRama\Kiosk\disable-premium-kiosk.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "C:\TurboRama\Kiosk\disable-premium-kiosk.ps1" -DisableUwf -DisableKeyboardFilter -DisableAutoLogon
)

echo [3/5] Restaurando kiosk TurboRama...
if exist "%~dp0TurboRamaFactoryShell.exe" (
    "%~dp0TurboRamaFactoryShell.exe" --restore-silent
) else if exist "C:\TurboRama\Kiosk\TurboRamaFactoryShell.exe" (
    "C:\TurboRama\Kiosk\TurboRamaFactoryShell.exe" --restore-silent
)

echo [4/5] Restaurando Shell e autologon...
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d explorer.exe /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f >nul
reg delete "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoLogonSID /f >nul 2>&1
reg add "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d explorer.exe /f >nul
reg add "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f >nul

echo [5/5] Verificando...
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell

echo.
echo  WINDOWS RESTAURADO! Reinicie o PC.
echo.
choice /C SN /M "Reiniciar agora (S=Sim N=Nao)"
if errorlevel 2 goto fim
shutdown /r /t 10 /c "Restaurando Windows normal"
:fim
pause