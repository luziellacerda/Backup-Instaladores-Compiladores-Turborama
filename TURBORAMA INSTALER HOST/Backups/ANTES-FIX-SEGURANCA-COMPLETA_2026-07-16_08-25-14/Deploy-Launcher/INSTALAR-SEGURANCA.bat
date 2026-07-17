@echo off
chcp 65001 >nul
title TurboRama - Seguranca IoT (CAD OFF / Ctrl+End ON)
net session >nul 2>&1
if errorlevel 1 (
  echo A pedir Administrador...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "EXE=C:\TurboRama\App\Launcher\TurboRama.Launcher.exe"
if not exist "%EXE%" (
  echo ERRO: %EXE% nao encontrado
  pause
  exit /b 1
)

echo ============================================
echo  Windows 10 IoT Enterprise - SEGURANCA
echo  Ctrl+Alt+Del BLOQUEADO (Keyboard Filter)
echo  Ctrl+End = menu TurboRama
echo ============================================
echo.

echo [1/5] Features Device Lockdown + Keyboard Filter (oficial Microsoft)...
dism.exe /Online /Enable-Feature /FeatureName:Client-DeviceLockdown /FeatureName:Client-KeyboardFilter /All /NoRestart
echo OK

echo [2/5] Servico MsKeyboardFilter AUTO (NAO iniciar agora)...
rem Em IoT, sc start antes do reboot reverte AUTO -^> DEMAND (evento 7040).
rem So definir Automatic; o servico sobe no proximo boot com o filtro no stack.
reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f >nul 2>nul
sc.exe config MsKeyboardFilter start= auto >nul 2>nul
sc.exe failure MsKeyboardFilter reset= 86400 actions= restart/3000/restart/5000/restart/10000 >nul 2>nul
echo OK

echo [3/5] Keyboard Filter: CAD Blocked, Ctrl+End Allowed...
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Alt+Del" /t REG_SZ /d Blocked /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+End" /t REG_SZ /d Allowed /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "DisableKeyboardFilterForAdministrators" /t REG_DWORD /d 0 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Windows" /t REG_SZ /d Blocked /f >nul 2>nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Win+L" /t REG_SZ /d Blocked /f >nul 2>nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Alt+Tab" /t REG_SZ /d Blocked /f >nul 2>nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Alt+F4" /t REG_SZ /d Blocked /f >nul 2>nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Esc" /t REG_SZ /d Blocked /f >nul 2>nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Shift+Ctrl+Esc" /t REG_SZ /d Blocked /f >nul 2>nul
echo OK

echo [4/5] Esvaziar CAD (policies)...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableChangePassword /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableLockWorkstation /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v HideFastUserSwitching /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoLogoff /t REG_DWORD /d 1 /f >nul
echo OK

echo [5/5] Agente TurboRama (Ctrl+End) no logon...
set "CMD="%EXE%" --security-agent"
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d %CMD% /f >nul
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d %CMD% /f >nul
schtasks /Delete /TN "TurboRamaSecurityAgent" /F >nul 2>nul
schtasks /Create /TN "TurboRamaSecurityAgent" /SC ONLOGON /RL LIMITED /F /TR "\"%EXE%\" --security-agent" >nul
taskkill /IM TurboRama.Launcher.exe /F >nul 2>nul
timeout /t 1 /nobreak >nul
start "" "%EXE%" --security-agent
timeout /t 2 /nobreak >nul

echo.
echo ============================================
echo  PRONTO (Windows 10 IoT) - FALTA REINICIAR
echo  - Ctrl+Alt+Del : Blocked no registo (activo apos reboot)
echo  - Ctrl+End     : menu TurboRama (ja activo)
echo  - PIN          : senha kiosk de fabrica
echo.
echo  OBRIGATORIO: reinicie o PC agora.
echo  Sem reboot o Windows ainda mostra o menu CAD.
echo ============================================
echo.
sc.exe qc MsKeyboardFilter
sc.exe query MsKeyboardFilter
echo.
if exist "C:\TurboRama\Logs\security-agent-alive.txt" (
  echo Agente:
  type "C:\TurboRama\Logs\security-agent-alive.txt"
)
echo.
choice /C SN /M "Reiniciar agora para activar o bloqueio de Ctrl+Alt+Del"
if errorlevel 2 goto :eof
if errorlevel 1 shutdown /r /t 5 /c "TurboRama: activar Keyboard Filter (bloquear Ctrl+Alt+Del)"

