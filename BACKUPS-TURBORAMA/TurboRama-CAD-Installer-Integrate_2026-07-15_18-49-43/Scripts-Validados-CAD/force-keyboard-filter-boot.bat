@echo off
reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f >nul
sc config MsKeyboardFilter start= auto >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Alt+Del" /t REG_SZ /d Blocked /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+End" /t REG_SZ /d Allowed /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "DisableKeyboardFilterForAdministrators" /t REG_DWORD /d 0 /f >nul
powershell -NoProfile -Command "try { $k=Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded | Where-Object { $_.Id -eq 'Ctrl+Alt+Del' }; if($k){ $k.Enabled=1; $k.Put()|Out-Null } } catch {}"
