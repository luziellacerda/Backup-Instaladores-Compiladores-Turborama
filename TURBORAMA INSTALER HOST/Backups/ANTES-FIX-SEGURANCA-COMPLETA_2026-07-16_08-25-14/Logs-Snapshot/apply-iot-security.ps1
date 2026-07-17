$ErrorActionPreference = "Continue"
$log = "C:\TurboRama\Logs\instalar-seguranca-apply.log"
New-Item -ItemType Directory -Force -Path "C:\TurboRama\Logs" | Out-Null
function L($m){ $t = (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " " + $m; Add-Content $log $t; Write-Output $t }

L "=== START IoT security apply ==="
L "User=$env:USERNAME Elevated=$(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"

# 1 DISM
L "[1] DISM Client-DeviceLockdown"
try { & dism.exe /Online /Enable-Feature /FeatureName:Client-DeviceLockdown /All /NoRestart 2>&1 | Out-String | ForEach-Object { L $_ } } catch { L "dism: $_" }

# 2 Service
L "[2] MsKeyboardFilter AUTO+START"
reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f | Out-Null
& sc.exe config MsKeyboardFilter start= auto 2>&1 | ForEach-Object { L "sc config: $_" }
& sc.exe start MsKeyboardFilter 2>&1 | ForEach-Object { L "sc start: $_" }

# 3 Keyboard Filter registry
L "[3] KeyboardFilter registry"
$kf = "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter"
reg add $kf /v "Ctrl+Alt+Del" /t REG_SZ /d Blocked /f | Out-Null
reg add $kf /v "Ctrl+End" /t REG_SZ /d Allowed /f | Out-Null
reg add $kf /v "DisableKeyboardFilterForAdministrators" /t REG_DWORD /d 0 /f | Out-Null
reg add $kf /v "Windows" /t REG_SZ /d Blocked /f | Out-Null
reg add $kf /v "Win+L" /t REG_SZ /d Blocked /f | Out-Null
reg add $kf /v "Alt+Tab" /t REG_SZ /d Blocked /f | Out-Null
reg add $kf /v "Alt+F4" /t REG_SZ /d Blocked /f | Out-Null
reg add $kf /v "Ctrl+Esc" /t REG_SZ /d Blocked /f | Out-Null
reg add $kf /v "Shift+Ctrl+Esc" /t REG_SZ /d Blocked /f | Out-Null
L "registry done"

# 4 Empty CAD policies
L "[4] Empty CAD policies"
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /t REG_DWORD /d 1 /f | Out-Null
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableChangePassword /t REG_DWORD /d 1 /f | Out-Null
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableLockWorkstation /t REG_DWORD /d 1 /f | Out-Null
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v HideFastUserSwitching /t REG_DWORD /d 1 /f | Out-Null
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v NoLogoff /t REG_DWORD /d 1 /f | Out-Null
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /t REG_DWORD /d 1 /f | Out-Null
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableChangePassword /t REG_DWORD /d 1 /f | Out-Null
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableLockWorkstation /t REG_DWORD /d 1 /f | Out-Null

# 5 WEKF WMI if available
L "[5] WEKF WMI"
try {
  $keys = Get-CimInstance -Namespace "root\standardcimv2\embedded" -ClassName WEKF_PredefinedKey -ErrorAction Stop
  foreach ($k in $keys) {
    if ($k.Id -match "Ctrl\+Alt\+Del|Ctrl\+Alt\+Delete") {
      $k.Enabled = $true
      Set-CimInstance -InputObject $k
      L "WEKF block $($k.Id)"
    }
  }
} catch { L "WEKF: $($_.Exception.Message)" }

# 6 Security agent
L "[6] Security agent"
$exe = "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe"
$cmd = "`"$exe`" --security-agent"
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d $cmd /f | Out-Null
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TurboRamaSecurityAgent /t REG_SZ /d $cmd /f | Out-Null
schtasks /Delete /TN "TurboRamaSecurityAgent" /F 2>$null | Out-Null
schtasks /Create /TN "TurboRamaSecurityAgent" /SC ONLOGON /RL LIMITED /F /TR "`"$exe`" --security-agent" 2>&1 | ForEach-Object { L "task: $_" }
Get-Process -Name "TurboRama.Launcher" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Start-Process -FilePath $exe -ArgumentList "--security-agent"
Start-Sleep -Seconds 2

L "=== VERIFY ==="
& sc.exe query MsKeyboardFilter 2>&1 | ForEach-Object { L $_ }
& sc.exe qc MsKeyboardFilter 2>&1 | ForEach-Object { L $_ }
$r = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter"
L "Ctrl+Alt+Del=$($r.'Ctrl+Alt+Del') Ctrl+End=$($r.'Ctrl+End') Windows=$($r.Windows)"
if (Test-Path "C:\TurboRama\Logs\security-agent-alive.txt") { L (Get-Content "C:\TurboRama\Logs\security-agent-alive.txt" -Raw) }
L "=== DONE ==="
