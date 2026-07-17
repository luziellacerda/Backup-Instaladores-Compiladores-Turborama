$log = "C:\TurboRama\Logs\kf-service-fix.log"
function L($m){ Add-Content $log ((Get-Date -Format "HH:mm:ss") + " " + $m); $m }

L "=== fix service start type ==="
# Registry Start = 2 (AUTO)
reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f
# Delayed auto sometimes more reliable
sc.exe config MsKeyboardFilter start= auto
sc.exe failure MsKeyboardFilter reset= 0 actions= restart/5000
Start-Sleep 1
$startVal = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter").Start
L "Registry Start=$startVal (2=AUTO 3=DEMAND)"
sc.exe qc MsKeyboardFilter

L "=== start service ==="
$r = sc.exe start MsKeyboardFilter 2>&1 | Out-String
L $r
Start-Sleep 3
$r2 = sc.exe query MsKeyboardFilter 2>&1 | Out-String
L $r2
$svc = Get-Service MsKeyboardFilter
L "Status=$($svc.Status) StartType=$($svc.StartType)"

# Event log last errors for this service
L "=== events ==="
try {
  Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddMinutes(-15)} -MaxEvents 80 -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'Keyboard|MsKeyboard|filtro de teclado|WEKF' -or $_.ProviderName -match 'Service Control Manager' -and $_.Id -in 7000,7001,7009,7023,7024,7031,7034,7040 } |
    Select-Object -First 15 TimeCreated,Id,Message |
    ForEach-Object { L ("EVT " + $_.TimeCreated + " id=" + $_.Id + " " + ($_.Message -replace "`r|`n"," ").Substring(0,[Math]::Min(200,$_.Message.Length))) }
} catch { L "events: $_" }

# Try WMI again after feature enable
L "=== WEKF ==="
try {
  Get-CimInstance -Namespace root\standardcimv2\embedded -ClassName WEKF_PredefinedKey -ErrorAction Stop |
    Where-Object { $_.Id -match 'Ctrl|Alt|Del|End' } |
    ForEach-Object { L ("WEKF " + $_.Id + " Enabled=" + $_.Enabled) }
} catch { L "WEKF: $($_.Exception.Message)" }

# Ensure agent still up
if (-not (Get-Process TurboRama.Launcher -ErrorAction SilentlyContinue)) {
  Start-Process "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe" -ArgumentList "--security-agent"
  Start-Sleep 1
}
L "Agent: $(Get-Content C:\TurboRama\Logs\security-agent-alive.txt -ErrorAction SilentlyContinue)"
L "DONE"
