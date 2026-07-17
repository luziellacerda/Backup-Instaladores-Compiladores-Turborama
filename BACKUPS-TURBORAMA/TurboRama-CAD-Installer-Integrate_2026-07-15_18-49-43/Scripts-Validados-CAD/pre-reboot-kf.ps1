$log = "C:\TurboRama\Logs\pre-reboot-kf.log"
function L($m){ Add-Content $log ((Get-Date -Format "HH:mm:ss")+" "+$m) }
Remove-Item $log -EA SilentlyContinue

# Do NOT start service - only lock AUTO so it loads at next boot
$svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter"
Set-ItemProperty $svcKey -Name Start -Value 2 -Type DWord -Force
sc.exe config MsKeyboardFilter start= auto | Out-Null
Set-Service MsKeyboardFilter -StartupType Automatic -EA SilentlyContinue
$w = Get-CimInstance Win32_Service -Filter "Name='MsKeyboardFilter'"
Invoke-CimMethod -InputObject $w -MethodName ChangeStartMode -Arguments @{StartMode="Automatic"} | Out-Null

# Ensure CAD blocked in reg
$kf = "HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter"
New-ItemProperty $kf -Name "Ctrl+Alt+Del" -Value "Blocked" -PropertyType String -Force | Out-Null
New-ItemProperty $kf -Name "Ctrl+End" -Value "Allowed" -PropertyType String -Force | Out-Null
New-ItemProperty $kf -Name "DisableKeyboardFilterForAdministrators" -Value 0 -PropertyType DWord -Force | Out-Null

Start-Sleep 1
$start = (Get-ItemProperty $svcKey).Start
$mode = (Get-CimInstance Win32_Service -Filter "Name='MsKeyboardFilter'").StartMode
L "RegStart=$start (want 2) StartMode=$mode (want Auto)"
L "CAD=$((Get-ItemProperty $kf).'Ctrl+Alt+Del')"

# Event log why service stopped earlier
try {
  Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddHours(-2)} -MaxEvents 200 -EA SilentlyContinue |
    Where-Object { $_.Message -match 'MsKeyboardFilter|Filtro de Teclado|Keyboard Filter' } |
    Select-Object -First 10 |
    ForEach-Object { L ("EVT id=$($_.Id) $($_.TimeCreated) $(($_.Message -replace '\s+',' ').Substring(0,[Math]::Min(180,$_.Message.Length)))") }
} catch { L "events: $_" }

# List related services/drivers
Get-Service | Where-Object { $_.Name -match 'key|filter|wekf|embed' -or $_.DisplayName -match 'teclado|Keyboard|Filter' } |
  ForEach-Object { L ("SVC $($_.Name) $($_.Status) $($_.StartType)") }

Get-ChildItem "$env:SystemRoot\System32\drivers" -Filter "*kbd*" -EA SilentlyContinue | ForEach-Object { L ("DRV $($_.Name)") }
Get-ChildItem "$env:SystemRoot\System32\drivers" -Filter "*key*" -EA SilentlyContinue | ForEach-Object { L ("DRV $($_.Name)") }

# Pending reboot marker
Set-Content "C:\TurboRama\Logs\REBOOT-NEEDED-FOR-CAD-BLOCK.txt" @"
REINICIE o PC para desactivar Ctrl+Alt+Del de verdade.
Depois do reboot:
- MsKeyboardFilter deve ficar Running
- Ctrl+Alt+Del nao abre o menu Windows
- Ctrl+End continua a abrir o menu TurboRama
"@ -Encoding UTF8

Set-Content "C:\TurboRama\Logs\pre-reboot-done.flag" (Get-Date -Format o)
