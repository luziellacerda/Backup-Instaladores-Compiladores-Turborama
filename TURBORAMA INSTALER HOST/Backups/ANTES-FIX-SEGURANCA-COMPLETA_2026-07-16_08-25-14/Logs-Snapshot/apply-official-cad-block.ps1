$ErrorActionPreference = "Continue"
$log = "C:\TurboRama\Logs\apply-official-cad-block.log"
New-Item -ItemType Directory -Force -Path "C:\TurboRama\Logs" | Out-Null
function L([string]$m) {
    $line = (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + "  " + $m
    Add-Content -Path $log -Value $line -Encoding UTF8
}

Remove-Item $log -ErrorAction SilentlyContinue
L "=== OFFICIAL IoT CAD BLOCK (Microsoft Keyboard Filter) ==="
L ("Elevated=" + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
L ("OS=" + (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion").ProductName)

# 1) DISM - BOTH features (Microsoft IoT lab)
L "[1] DISM Client-DeviceLockdown + Client-KeyboardFilter"
$dism = & dism.exe /Online /Enable-Feature /FeatureName:Client-DeviceLockdown /FeatureName:Client-KeyboardFilter /All /NoRestart 2>&1 | Out-String
L $dism.Trim()

foreach ($f in @("Client-DeviceLockdown", "Client-KeyboardFilter")) {
    $info = & dism.exe /Online /Get-FeatureInfo /FeatureName:$f 2>&1 | Out-String
    foreach ($line in ($info -split "`n")) {
        if ($line -match "State|Estado|Feature Name|Nome do recurso|Nome para") {
            L ("  " + $line.Trim())
        }
    }
}

# 2) Service AUTO only - do NOT sc start before reboot
L "[2] MsKeyboardFilter = Automatic (no forced start)"
$svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter"
if (Test-Path $svcKey) {
    Set-ItemProperty -Path $svcKey -Name Start -Value 2 -Type DWord -Force
    & sc.exe config MsKeyboardFilter start= auto 2>&1 | ForEach-Object { L ("sc: " + $_) }
    try {
        Set-Service -Name MsKeyboardFilter -StartupType Automatic -ErrorAction Stop
        L "Set-Service Automatic OK"
    }
    catch {
        L ("Set-Service: " + $_.Exception.Message)
    }
    try {
        $w = Get-CimInstance Win32_Service -Filter "Name='MsKeyboardFilter'"
        $c = Invoke-CimMethod -InputObject $w -MethodName ChangeStartMode -Arguments @{ StartMode = "Automatic" }
        L ("WMI StartMode Return=" + $c.ReturnValue)
    }
    catch {
        L ("WMI: " + $_.Exception.Message)
    }
    & sc.exe failure MsKeyboardFilter reset= 86400 actions= restart/3000/restart/5000/restart/10000 2>&1 | ForEach-Object { L ("failure: " + $_) }
    $svc = Get-Service MsKeyboardFilter
    L ("RegStart=" + (Get-ItemProperty $svcKey).Start + " StartType=" + $svc.StartType + " Status=" + $svc.Status)
}
else {
    L "MsKeyboardFilter service key missing - needs reboot after feature enable"
}

# 3) Registry KeyboardFilter
L "[3] Registry KeyboardFilter"
$kfPath = "HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter"
if (-not (Test-Path $kfPath)) {
    New-Item -Path $kfPath -Force | Out-Null
}
New-ItemProperty $kfPath -Name "Ctrl+Alt+Del" -Value "Blocked" -PropertyType String -Force | Out-Null
New-ItemProperty $kfPath -Name "Ctrl+End" -Value "Allowed" -PropertyType String -Force | Out-Null
New-ItemProperty $kfPath -Name "DisableKeyboardFilterForAdministrators" -Value 0 -PropertyType DWord -Force | Out-Null
foreach ($k in @("Windows", "Win+L", "Alt+Tab", "Alt+F4", "Ctrl+Esc", "Shift+Ctrl+Esc")) {
    New-ItemProperty $kfPath -Name $k -Value "Blocked" -PropertyType String -Force | Out-Null
}
L ("CAD=" + (Get-ItemProperty $kfPath)."Ctrl+Alt+Del" + " End=" + (Get-ItemProperty $kfPath)."Ctrl+End")

# 4) WEKF WMI official method
L "[4] WEKF_PredefinedKey (official)"
function Enable-Predefined-Key([string]$Id) {
    try {
        $predefined = Get-WmiObject -Class WEKF_PredefinedKey -Namespace "root\standardcimv2\embedded" -ErrorAction Stop |
            Where-Object { $_.Id -eq $Id }
        if ($predefined) {
            $predefined.Enabled = 1
            $predefined.Put() | Out-Null
            L ("WEKF Enabled (blocked): " + $Id)
            return $true
        }
        L ("WEKF key not found: " + $Id)
        return $false
    }
    catch {
        L ("WEKF unavailable: " + $_.Exception.Message)
        return $false
    }
}

$wekfOk = $false
foreach ($id in @("Ctrl+Alt+Del", "Ctrl+Alt+Delete", "Ctrl+Esc", "Win+L", "Alt+Tab", "Alt+F4", "Shift+Ctrl+Esc")) {
    if (Enable-Predefined-Key $id) {
        if ($id -like "Ctrl+Alt+Del*") {
            $wekfOk = $true
        }
    }
}

try {
    $end = Get-WmiObject -Class WEKF_PredefinedKey -Namespace "root\standardcimv2\embedded" -ErrorAction Stop |
        Where-Object { $_.Id -eq "Ctrl+End" }
    if ($end) {
        $end.Enabled = 0
        $end.Put() | Out-Null
        L "WEKF Allowed: Ctrl+End"
    }
}
catch { }

# 5) Empty CAD policies
L "[5] Empty CAD policies"
foreach ($p in @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
    )) {
    if (-not (Test-Path $p)) {
        New-Item $p -Force | Out-Null
    }
    New-ItemProperty $p -Name DisableTaskMgr -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $p -Name DisableChangePassword -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $p -Name DisableLockWorkstation -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $p -Name HideFastUserSwitching -Value 1 -PropertyType DWord -Force | Out-Null
}
$exp = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer"
if (-not (Test-Path $exp)) {
    New-Item $exp -Force | Out-Null
}
New-ItemProperty $exp -Name NoLogoff -Value 1 -PropertyType DWord -Force | Out-Null

# 6) Boot task
L "[6] Boot task"
$bootLines = @(
    "@echo off",
    'reg add "HKLM\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" /v Start /t REG_DWORD /d 2 /f >nul',
    "sc config MsKeyboardFilter start= auto >nul",
    'reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+Alt+Del" /t REG_SZ /d Blocked /f >nul',
    'reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "Ctrl+End" /t REG_SZ /d Allowed /f >nul',
    'reg add "HKLM\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" /v "DisableKeyboardFilterForAdministrators" /t REG_DWORD /d 0 /f >nul',
    'powershell -NoProfile -Command "try { $k=Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded | Where-Object { $_.Id -eq ''Ctrl+Alt+Del'' }; if($k){ $k.Enabled=1; $k.Put()|Out-Null } } catch {}"'
)
Set-Content -Path "C:\TurboRama\Logs\force-keyboard-filter-boot.bat" -Value $bootLines -Encoding ASCII
& schtasks.exe /Delete /TN "TurboRamaForceKeyboardFilter" /F 2>$null | Out-Null
& schtasks.exe /Create /TN "TurboRamaForceKeyboardFilter" /SC ONSTART /RU SYSTEM /RL HIGHEST /F /TR "C:\TurboRama\Logs\force-keyboard-filter-boot.bat" 2>&1 | ForEach-Object { L ("task: " + $_) }

# 7) Security agent
L "[7] Security agent"
$exe = "C:\TurboRama\App\Launcher\TurboRama.Launcher.exe"
if (Test-Path $exe) {
    $cmd = '"' + $exe + '" --security-agent'
    New-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name TurboRamaSecurityAgent -Value $cmd -PropertyType String -Force | Out-Null
    New-ItemProperty "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name TurboRamaSecurityAgent -Value $cmd -PropertyType String -Force | Out-Null
    & schtasks.exe /Delete /TN "TurboRamaSecurityAgent" /F 2>$null | Out-Null
    $tr = '"' + $exe + '" --security-agent'
    & schtasks.exe /Create /TN "TurboRamaSecurityAgent" /SC ONLOGON /RL LIMITED /F /TR $tr 2>&1 | ForEach-Object { L ("agent-task: " + $_) }
    Get-Process TurboRama.Launcher -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 1
    Start-Process -FilePath $exe -ArgumentList "--security-agent"
    Start-Sleep 2
    if (Test-Path "C:\TurboRama\Logs\security-agent-alive.txt") {
        L ("agent: " + (Get-Content "C:\TurboRama\Logs\security-agent-alive.txt" -Raw).Trim())
    }
}

# 8) Post-reboot WEKF script
$post = @'
$log = "C:\TurboRama\Logs\post-reboot-wekf.log"
function L($m) { Add-Content $log ((Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " " + $m) }
L "=== post-reboot WEKF ==="
sc.exe config MsKeyboardFilter start= auto | Out-Null
$s = Get-Service MsKeyboardFilter -ErrorAction SilentlyContinue
L ("Service " + $s.Status + " " + $s.StartType)
try {
    $k = Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded |
        Where-Object { $_.Id -eq "Ctrl+Alt+Del" }
    if ($k) {
        $k.Enabled = 1
        $k.Put() | Out-Null
        L "Blocked Ctrl+Alt+Del WEKF OK"
    }
    else {
        $all = Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded |
            Select-Object -ExpandProperty Id
        L ("keys sample: " + (($all | Select-Object -First 20) -join ", "))
        $k2 = Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded |
            Where-Object { $_.Id -match "Ctrl\+Alt\+Del" }
        if ($k2) {
            $k2.Enabled = 1
            $k2.Put() | Out-Null
            L ("Blocked " + $k2.Id)
        }
    }
}
catch {
    L ("WEKF: " + $_.Exception.Message)
}
L "=== done ==="
'@
Set-Content -Path "C:\TurboRama\Logs\post-reboot-wekf.ps1" -Value $post -Encoding UTF8
& schtasks.exe /Delete /TN "TurboRamaPostRebootWEKF" /F 2>$null | Out-Null
& schtasks.exe /Create /TN "TurboRamaPostRebootWEKF" /SC ONSTART /RU SYSTEM /RL HIGHEST /F /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\TurboRama\Logs\post-reboot-wekf.ps1" 2>&1 | ForEach-Object { L ("post-task: " + $_) }

L "=== FINAL SNAPSHOT ==="
$svcF = Get-Service MsKeyboardFilter -ErrorAction SilentlyContinue
L ("MsKeyboardFilter StartType=" + $svcF.StartType + " Status=" + $svcF.Status)
L ("RegStart=" + (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter" -ErrorAction SilentlyContinue).Start)
L ("WEKF applied now=" + $wekfOk)
L "NEXT: REBOOT required. After reboot Ctrl+Alt+Del blocked; Ctrl+End = TurboRama menu."
L "=== DONE ==="
Set-Content -Path "C:\TurboRama\Logs\apply-official-cad-done.flag" -Value (Get-Date -Format o) -Encoding ASCII
