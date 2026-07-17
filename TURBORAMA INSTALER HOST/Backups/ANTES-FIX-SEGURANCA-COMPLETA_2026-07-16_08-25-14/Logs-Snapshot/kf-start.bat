reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f > C:\TurboRama\Logs\kf-start.txt 2>&1
sc config MsKeyboardFilter start= auto >> C:\TurboRama\Logs\kf-start.txt 2>&1
sc start MsKeyboardFilter >> C:\TurboRama\Logs\kf-start.txt 2>&1
timeout /t 2 /nobreak >nul
sc query MsKeyboardFilter >> C:\TurboRama\Logs\kf-start.txt 2>&1
sc qc MsKeyboardFilter >> C:\TurboRama\Logs\kf-start.txt 2>&1
reg query "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start >> C:\TurboRama\Logs\kf-start.txt 2>&1
