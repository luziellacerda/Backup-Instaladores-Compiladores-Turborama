@echo off
chcp 65001 >nul
title TurboRama - Seguranca IoT COMPLETA
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
echo  Windows 10 IoT - SEGURANCA COMPLETA
echo  Keyboard Filter + Agent + Keep-alive
echo ============================================
echo.

echo [1/6] Features DeviceLockdown + KeyboardFilter...
dism.exe /Online /Enable-Feature /FeatureName:Client-DeviceLockdown /FeatureName:Client-KeyboardFilter /All /NoRestart
echo OK

echo [2/6] MsKeyboardFilter = Automatic ^(sem start forcado^)...
reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f >nul
sc.exe config MsKeyboardFilter start= auto >nul
sc.exe failure MsKeyboardFilter reset= 86400 actions= restart/3000/restart/5000/restart/10000 >nul
echo OK

echo [3/6] Registry KeyboardFilter...
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

echo [4/6] Esvaziar CAD policies...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableChangePassword /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableLockWorkstation /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v HideFastUserSwitching /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoLogoff /t REG_DWORD /d 1 /f >nul
echo OK

echo [5/6] WEKF WMI ^(se classe existir^)...
powershell -NoProfile -Command "try { Get-WmiObject -Namespace root\standardcimv2\embedded -Class WEKF_PredefinedKey | ForEach-Object { if($_.Id -eq 'Ctrl+Alt+Del'){ $_.Enabled=$true; $_.Put()|Out-Null; 'CAD blocked' } if($_.Id -match 'Ctrl\+Esc|Win\+L|Alt\+Tab|Alt\+F4|Shift\+Ctrl\+Esc|Windows'){ $_.Enabled=$true; $_.Put()|Out-Null } } } catch { 'WEKF later/reboot: '+$_.Exception.Message }"
echo OK

echo [6/6] Agent + keep-alive 2min...
set "CMD=\"%EXE%\" --security-agent"
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d %CMD% /f >nul
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d %CMD% /f >nul
schtasks /Delete /TN "TurboRamaSecurityAgent" /F >nul 2>nul
schtasks /Create /TN "TurboRamaSecurityAgent" /SC ONLOGON /RL LIMITED /F /TR "\"%EXE%\" --security-agent" >nul
schtasks /Delete /TN "TurboRamaSecurityAgentKeepAlive" /F >nul 2>nul
schtasks /Create /TN "TurboRamaSecurityAgentKeepAlive" /SC MINUTE /MO 2 /RL LIMITED /F /TR "\"%EXE%\" --security-agent" >nul
schtasks /Delete /TN "TurboRamaForceKeyboardFilter" /F >nul 2>nul
echo @echo off> "%TEMP%\tr-kf-boot.bat"
echo reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f ^>nul>> "%TEMP%\tr-kf-boot.bat"
echo sc config MsKeyboardFilter start= auto ^>nul>> "%TEMP%\tr-kf-boot.bat"
echo reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Alt+Del" /t REG_SZ /d Blocked /f ^>nul>> "%TEMP%\tr-kf-boot.bat"
copy /Y "%TEMP%\tr-kf-boot.bat" "C:\TurboRama\Logs\force-keyboard-filter-boot.bat" >nul
schtasks /Create /TN "TurboRamaForceKeyboardFilter" /SC ONSTART /RU SYSTEM /RL HIGHEST /F /TR "C:\TurboRama\Logs\force-keyboard-filter-boot.bat" >nul 2>nul
taskkill /IM TurboRama.Launcher.exe /F >nul 2>nul
timeout /t 1 /nobreak >nul
start "" "%EXE%" --security-agent
timeout /t 2 /nobreak >nul

echo.
echo ============================================
echo  PRONTO
echo  Ctrl+Alt+Del : bloqueado ^(filtro^)
echo  Ctrl+End     : menu TurboRama
echo  PIN menu     : Lz2026@$ ^(senha kiosk^)
echo  Keep-alive   : cada 2 minutos
echo ============================================
sc query MsKeyboardFilter
echo.
if exist "C:\TurboRama\Logs\security-agent-alive.txt" type "C:\TurboRama\Logs\security-agent-alive.txt"
echo.
choice /C SN /M "Reiniciar agora para reforcar filtro no boot"
if errorlevel 2 goto :eof
if errorlevel 1 shutdown /r /t 8 /c "TurboRama seguranca IoT"
