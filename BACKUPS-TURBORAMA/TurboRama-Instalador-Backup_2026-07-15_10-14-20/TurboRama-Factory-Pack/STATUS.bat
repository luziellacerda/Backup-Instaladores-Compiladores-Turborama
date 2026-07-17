@echo off
chcp 65001 >nul
echo === Services ===
sc query TurboRamaWatchdog
sc query TurboRamaMaintenance
echo.
echo === Autologon ===
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName
echo.
echo === Arcade account ===
net user Arcade 2>nul || echo Arcade missing
echo.
echo === Lock ===
if exist C:\TurboRama\State\maintenance.lock (type C:\TurboRama\State\maintenance.lock) else (echo no lock)
pause