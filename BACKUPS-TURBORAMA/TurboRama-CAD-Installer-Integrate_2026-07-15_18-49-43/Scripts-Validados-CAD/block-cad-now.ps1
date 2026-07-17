$ErrorActionPreference = "Continue"
$log = "C:\TurboRama\Logs\block-cad-now.log"
New-Item -ItemType Directory -Force -Path "C:\TurboRama\Logs" | Out-Null
function L([string]$m) {
  $line = (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + "  " + $m
  Add-Content -Path $log -Value $line -Encoding UTF8
  Write-Output $line
}

L "=== BLOCK CAD IoT START ==="
L ("Elevated=" + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
L ("OS=" + (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion").ProductName)

# 1) Feature
L "[1] Enable Client-DeviceLockdown"
$dism = & dism.exe /Online /Enable-Feature /FeatureName:Client-DeviceLockdown /All /NoRestart 2>&1 | Out-String
L $dism.Trim()
$info = & dism.exe /Online /Get-FeatureInfo /FeatureName:Client-DeviceLockdown 2>&1 | Out-String
foreach ($line in ($info -split "`n")) {
  if ($line -match "State|Estado|Feature Name|Nome") { L ("  " + $line.Trim()) }
}

# 2) Force service AUTO via registry + sc + WMI
L "[2] Force MsKeyboardFilter Automatic"
try {
  $svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter"
  Set-ItemProperty -Path $svcKey -Name Start -Value 2 -Type DWord -Force
  # DelayedAutoStart optional off
  if (Get-ItemProperty -Path $svcKey -Name DelayedAutostart -EA SilentlyContinue) {
    Set-ItemProperty -Path $svcKey -Name DelayedAutostart -Value 0 -Type DWord -Force
  }
  L ("Reg Start now=" + (Get-ItemProperty $svcKey).Start)
} catch { L ("reg: " + $_.Exception.Message) }

& sc.exe config MsKeyboardFilter start= auto 2>&1 | ForEach-Object { L ("sc config: " + $_) }
try {
  Set-Service -Name MsKeyboardFilter -StartupType Automatic -ErrorAction Stop
  L "Set-Service Automatic OK"
} catch { L ("Set-Service: " + $_.Exception.Message) }

try {
  $w = Get-CimInstance Win32_Service -Filter "Name='MsKeyboardFilter'"
  $c = Invoke-CimMethod -InputObject $w -MethodName ChangeStartMode -Arguments @{StartMode="Automatic"}
  L ("WMI ChangeStartMode ReturnValue=" + $c.ReturnValue)
} catch { L ("WMI startmode: " + $_.Exception.Message) }

# failure recovery: restart on fail
& sc.exe failure MsKeyboardFilter reset= 86400 actions= restart/3000/restart/5000/restart/10000 2>&1 | ForEach-Object { L ("sc failure: " + $_) }

# 3) Start service now
L "[3] Start MsKeyboardFilter"
& sc.exe stop MsKeyboardFilter 2>&1 | Out-Null
Start-Sleep -Seconds 1
$startOut = & sc.exe start MsKeyboardFilter 2>&1 | Out-String
L $startOut.Trim()
Start-Sleep -Seconds 3
$q = & sc.exe query MsKeyboardFilter 2>&1 | Out-String
L $q.Trim()
$qc = & sc.exe qc MsKeyboardFilter 2>&1 | Out-String
L $qc.Trim()

# 4) Registry filter keys (official IoT names)
L "[4] KeyboardFilter registry"
$kfPath = "HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter"
if (-not (Test-Path $kfPath)) { New-Item -Path $kfPath -Force | Out-Null }
$map = @{
  "Ctrl+Alt+Del" = "Blocked"
  "Ctrl+End" = "Allowed"
  "Windows" = "Blocked"
  "Win+L" = "Blocked"
  "Alt+Tab" = "Blocked"
  "Alt+F4" = "Blocked"
  "Ctrl+Esc" = "Blocked"
  "Shift+Ctrl+Esc" = "Blocked"
  "Ctrl+Shift+Esc" = "Blocked"
}
foreach ($k in $map.Keys) {
  New-ItemProperty -Path $kfPath -Name $k -Value $map[$k] -PropertyType String -Force | Out-Null
}
New-ItemProperty -Path $kfPath -Name "DisableKeyboardFilterForAdministrators" -Value 0 -PropertyType DWord -Force | Out-Null
L ("CAD=" + (Get-ItemProperty $kfPath)."Ctrl+Alt+Del" + " End=" + (Get-ItemProperty $kfPath)."Ctrl+End")

# 5) WEKF WMI
L "[5] WEKF WMI"
try {
  $keys = Get-CimInstance -Namespace "root\standardcimv2\embedded" -ClassName WEKF_PredefinedKey -ErrorAction Stop
  $n = 0
  foreach ($k in $keys) {
    $id = [string]$k.Id
    if ($id -match "Ctrl\+Alt\+Del|Ctrl\+Alt\+Delete") {
      $k.Enabled = $true
      Set-CimInstance -InputObject $k
      L ("WEKF BLOCK " + $id + " Enabled=" + $k.Enabled)
      $n++
    }
    if ($id -match "Ctrl\+End") {
      $k.Enabled = $false
      Set-CimInstance -InputObject $k
      L ("WEKF ALLOW " + $id)
    }
  }
  if ($n -eq 0) { L "WEKF: no CAD key found among predefined keys" }
  # also try classic WMI Put
  try {
    $old = Get-WmiObject -Namespace "root\standardcimv2\embedded" -Class WEKF_PredefinedKey -ErrorAction Stop |
      Where-Object { $_.Id -eq "Ctrl+Alt+Del" }
    if ($old) {
      $old.Enabled = $true
      $old.Put() | Out-Null
      L "WEKF classic Put Ctrl+Alt+Del Enabled=true"
    }
  } catch { L ("WEKF classic: " + $_.Exception.Message) }
} catch {
  L ("WEKF unavailable until reboot: " + $_.Exception.Message)
}

# 6) Empty CAD policies (if SAS still flashes, no useful actions)
L "[6] Empty CAD policies"
$paths = @(
  "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
  "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
)
foreach ($p in $paths) {
  if (-not (Test-Path $p)) { New-Item $p -Force | Out-Null }
  New-ItemProperty $p -Name DisableTaskMgr -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty $p -Name DisableChangePassword -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty $p -Name DisableLockWorkstation -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty $p -Name HideFastUserSwitching -Value 1 -PropertyType DWord -Force | Out-Null
}
$exp = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer"
if (-not (Test-Path $exp)) { New-Item $exp -Force | Out-Null }
New-ItemProperty $exp -Name NoLogoff -Value 1 -PropertyType DWord -Force | Out-Null

# 7) Boot task: force filter service every boot (before user)
L "[7] Boot task force filter"
$bootBat = @"
@echo off
reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f >nul
sc config MsKeyboardFilter start= auto >nul
sc start MsKeyboardFilter >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Alt+Del" /t REG_SZ /d Blocked /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+End" /t REG_SZ /d Allowed /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "DisableKeyboardFilterForAdministrators" /t REG_DWORD /d 0 /f >nul
"@
Set-Content -Path "C:\TurboRama\Logs\force-keyboard-filter-boot.bat" -Value $bootBat -Encoding ASCII
& schtasks.exe /Delete /TN "TurboRamaForceKeyboardFilter" /F 2>$null | Out-Null
$tr = "C:\TurboRama\Logs\force-keyboard-filter-boot.bat"
& schtasks.exe /Create /TN "TurboRamaForceKeyboardFilter" /SC ONSTART /RU SYSTEM /RL HIGHEST /F /TR $tr 2>&1 | ForEach-Object { L ("task: " + $_) }

# 8) Security agent
L "[8] Security agent"
$exe = "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe"
if (Test-Path $exe) {
  $cmd = "`"$exe`" --security-agent"
  New-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name TurboRamaSecurityAgent -Value $cmd -PropertyType String -Force | Out-Null
  New-ItemProperty "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name TurboRamaSecurityAgent -Value $cmd -PropertyType String -Force | Out-Null
  & schtasks.exe /Delete /TN "TurboRamaSecurityAgent" /F 2>$null | Out-Null
  & schtasks.exe /Create /TN "TurboRamaSecurityAgent" /SC ONLOGON /RL LIMITED /F /TR "`"$exe`" --security-agent" 2>&1 | ForEach-Object { L ("agent task: " + $_) }
  Get-Process TurboRama.Launcher -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
  Start-Sleep 1
  Start-Process -FilePath $exe -ArgumentList "--security-agent"
  Start-Sleep 2
  if (Test-Path "C:\TurboRama\Logs\security-agent-alive.txt") {
    L ("agent: " + (Get-Content "C:\TurboRama\Logs\security-agent-alive.txt" -Raw).Trim())
  }
} else {
  L "Launcher missing"
}

# Final verify
L "=== FINAL ==="
$svc = Get-Service MsKeyboardFilter
L ("Service Status=" + $svc.Status + " StartType=" + $svc.StartType)
L ("RegStart=" + (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter").Start)
$r = Get-ItemProperty $kfPath
L ("Filter CAD=" + $r."Ctrl+Alt+Del" + " End=" + $r."Ctrl+End" + " KBF=" + $r.KBFServiceIsRunning)

# Pending reboot flag for feature
$needReboot = $true
try {
  $fi = & dism.exe /Online /Get-FeatureInfo /FeatureName:Client-DeviceLockdown 2>&1 | Out-String
  if ($fi -match "Enabled|Habilitado") { L "Feature appears Enabled" }
} catch {}
L "REBOOT REQUIRED for Ctrl+Alt+Del kernel filter to fully block SAS"
L "=== DONE ==="

# Write marker for parent process
Set-Content "C:\TurboRama\Logs\block-cad-done.flag" -Value (Get-Date -Format o) -Encoding ASCII
