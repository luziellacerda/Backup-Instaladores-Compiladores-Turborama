$k="HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter"
Set-ItemProperty $k -Name Start -Value 2 -Type DWord -Force
sc.exe config MsKeyboardFilter start= auto | Out-Null
Set-Content "C:\TurboRama\Logs\pre-reboot-auto.flag" ((Get-Date -Format o) + " Start=" + (Get-ItemProperty $k).Start)
shutdown.exe /r /t 5 /c "TurboRama: activar Keyboard Filter - bloquear Ctrl+Alt+Del"
