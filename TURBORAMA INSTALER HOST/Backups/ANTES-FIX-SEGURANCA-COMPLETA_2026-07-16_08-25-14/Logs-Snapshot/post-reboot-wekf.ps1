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
