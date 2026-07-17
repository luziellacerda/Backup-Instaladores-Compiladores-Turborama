using System.Management;
using System.Text;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Optional;
using WinReg = Microsoft.Win32;

namespace TurboRama.Windows.Security;

/// <summary>
/// Windows 10 IoT Enterprise: desactiva Ctrl+Alt+Del de verdade
/// (Keyboard Filter / MsKeyboardFilter / WEKF) e esvazia o CAD se ainda abrir.
/// Menu TurboRama = Ctrl+End (Allowed no filtro; capturado pelo SecurityAgent).
/// Fluxo validado em campo (2026-07-15): DISM → AUTO sem sc start → reg → WEKF →
/// tarefa ONSTART + pós-reboot WEKF.
/// </summary>
public static class CadBlockService
{
    public const string ForceBootTaskName = "TurboRamaForceKeyboardFilter";
    public const string PostRebootTaskName = "TurboRamaPostRebootWEKF";

    public static OperationResult ApplySystemWide()
    {
        ProductPaths.EnsureLayout();
        var notes = new List<string>();
        int ok = 0;

        notes.Add("Target=Windows 10 IoT Enterprise (Keyboard Filter)");

        // 0) Features DISM + serviço Automatic (sem sc start pré-reboot)
        OperationResult kfEnable = KeyboardFilterModuleService.Enable();
        notes.Add("KbFilter.Enable: " + kfEnable.Message);
        if (kfEnable.Success)
        {
            ok++;
        }

        EnsureKeyboardFilterAutoHard(notes);

        // 1) Esvaziar CAD (políticas) — reforço se o filtro falhar a meio
        if (ApplyEmptyCad(WinReg.Registry.LocalMachine, notes))
        {
            ok++;
        }

        try
        {
            if (ApplyEmptyCad(WinReg.Registry.CurrentUser, notes))
            {
                ok++;
            }
        }
        catch (Exception ex)
        {
            notes.Add("HKCU: " + ex.Message);
        }

        // 2) Keyboard Filter registry (IoT — nomes oficiais do filtro)
        if (ApplyKeyboardFilterRegistry(notes))
        {
            ok++;
        }

        // 3) WEKF WMI (embedded) — bloqueia Ctrl+Alt+Del no filtro (pode falhar pré-reboot)
        bool wekfNow = TryWekfBlockKeys(notes);
        if (wekfNow)
        {
            ok++;
        }

        // 4) Tarefa ONSTART + script pós-reboot (garantem filtro após reinício)
        if (RegisterPersistenceScriptsAndTasks(notes))
        {
            ok++;
        }

        // 5) Log de instalação
        WriteApplyLog(notes, wekfNow);

        string msg = "CadBlock IoT ok=" + ok + " wekfNow=" + wekfNow +
                     " | REBOOT se MsKeyboardFilter ainda não Running | " +
                     string.Join("; ", notes);
        return ok > 0
            ? OperationResult.Ok(msg, "CadBlockService.ApplySystemWide")
            : OperationResult.Fail(msg, "CAD_BLOCK", "CadBlockService.ApplySystemWide");
    }

    private static bool ApplyEmptyCad(WinReg.RegistryKey root, List<string> notes)
    {
        try
        {
            using WinReg.RegistryKey? sys = root.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true)
                ?? root.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
            if (sys == null)
            {
                return false;
            }

            sys.SetValue("DisableTaskMgr", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("DisableChangePassword", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("DisableLockWorkstation", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("HideFastUserSwitching", 1, WinReg.RegistryValueKind.DWord);

            using WinReg.RegistryKey? exp = root.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true)
                ?? root.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
            exp?.SetValue("NoLogoff", 1, WinReg.RegistryValueKind.DWord);

            notes.Add("CAD esvaziado " + root.Name);
            return true;
        }
        catch (Exception ex)
        {
            notes.Add("EmptyCAD: " + ex.Message);
            return false;
        }
    }

    private static bool ApplyKeyboardFilterRegistry(List<string> notes)
    {
        try
        {
            using WinReg.RegistryKey? kf = WinReg.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter", true);
            if (kf == null)
            {
                notes.Add("KeyboardFilter registry indisponível");
                return false;
            }

            // SAS bloqueado (nome exacto no IoT LTSC)
            kf.SetValue("Ctrl+Alt+Del", "Blocked", WinReg.RegistryValueKind.String);
            // Menu TurboRama (substituto) — custom allowed
            kf.SetValue("Ctrl+End", "Allowed", WinReg.RegistryValueKind.String);
            // Filtro activo também para administradores no kiosk de fábrica
            kf.SetValue("DisableKeyboardFilterForAdministrators", 0, WinReg.RegistryValueKind.DWord);

            // Nomes oficiais do Keyboard Filter IoT (validado em campo)
            TrySet(kf, "Windows", "Blocked");
            TrySet(kf, "Win+L", "Blocked");
            TrySet(kf, "Alt+F4", "Blocked");
            TrySet(kf, "Alt+Tab", "Blocked");
            TrySet(kf, "Ctrl+Esc", "Blocked");
            TrySet(kf, "Shift+Ctrl+Esc", "Blocked");

            notes.Add("KeyboardFilter registry: CAD=Blocked Ctrl+End=Allowed");
            return true;
        }
        catch (Exception ex)
        {
            notes.Add("KeyboardFilter reg: " + ex.Message);
            return false;
        }
    }

    private static void TrySet(WinReg.RegistryKey kf, string name, string value)
    {
        try
        {
            kf.SetValue(name, value, WinReg.RegistryValueKind.String);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// WEKF_PredefinedKey (root\standardcimv2\embedded) — IoT Keyboard Filter WMI.
    /// Enabled=true significa tecla BLOQUEADA no filtro.
    /// Pré-reboot a classe pode não existir ainda (feature acabou de ser activada).
    /// </summary>
    private static bool TryWekfBlockKeys(List<string> notes)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\standardcimv2\embedded",
                "SELECT * FROM WEKF_PredefinedKey");
            using ManagementObjectCollection results = searcher.Get();
            int hit = 0;
            foreach (ManagementBaseObject raw in results)
            {
                using var obj = (ManagementObject)raw;
                string id = (obj["Id"] as string) ?? "";
                bool isCad =
                    id.Contains("Ctrl+Alt+Del", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("Ctrl+Alt+Delete", StringComparison.OrdinalIgnoreCase) ||
                    id.Equals("CAD", StringComparison.OrdinalIgnoreCase) ||
                    (id.Contains("Alt+Del", StringComparison.OrdinalIgnoreCase) &&
                     id.Contains("Ctrl", StringComparison.OrdinalIgnoreCase));

                bool isCtrlEnd =
                    id.Contains("Ctrl+End", StringComparison.OrdinalIgnoreCase);

                bool isOtherBlock =
                    id.Equals("Ctrl+Esc", StringComparison.OrdinalIgnoreCase) ||
                    id.Equals("Win+L", StringComparison.OrdinalIgnoreCase) ||
                    id.Equals("Alt+Tab", StringComparison.OrdinalIgnoreCase) ||
                    id.Equals("Alt+F4", StringComparison.OrdinalIgnoreCase) ||
                    id.Equals("Shift+Ctrl+Esc", StringComparison.OrdinalIgnoreCase) ||
                    id.Equals("Windows", StringComparison.OrdinalIgnoreCase);

                if (isCad || isOtherBlock)
                {
                    obj["Enabled"] = true; // bloqueado
                    obj.Put();
                    hit++;
                    notes.Add("WEKF block: " + id);
                }
                else if (isCtrlEnd)
                {
                    obj["Enabled"] = false; // permitido
                    obj.Put();
                    notes.Add("WEKF allow: " + id);
                }
            }

            if (hit == 0)
            {
                notes.Add("WEKF: nenhum Id CAD (reg+reboot aplicam o bloqueio)");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            notes.Add("WEKF (pré-reboot ok se feature nova): " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Garante Start=Automatic por várias vias (reg + sc + WMI).
    /// Não chama sc start antes do reboot (SCM reverte AUTO→DEMAND no IoT).
    /// </summary>
    private static void EnsureKeyboardFilterAutoHard(List<string> notes)
    {
        try
        {
            using WinReg.RegistryKey? svc = WinReg.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\MsKeyboardFilter", true);
            svc?.SetValue("Start", 2, WinReg.RegistryValueKind.DWord); // AUTO
            ProcessRunner.Run("sc.exe", "config MsKeyboardFilter start= auto", operationName: "cad-kf-auto");
            ProcessRunner.Run(
                "sc.exe",
                "failure MsKeyboardFilter reset= 86400 actions= restart/3000/restart/5000/restart/10000",
                operationName: "cad-kf-failure");

            // WMI ChangeStartMode (reforço extra — validado no script oficial)
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_Service WHERE Name='MsKeyboardFilter'");
                foreach (ManagementBaseObject raw in searcher.Get())
                {
                    using var mo = (ManagementObject)raw;
                    var inParams = mo.GetMethodParameters("ChangeStartMode");
                    inParams["StartMode"] = "Automatic";
                    ManagementBaseObject outParams = mo.InvokeMethod("ChangeStartMode", inParams, null!);
                    object? rv = outParams?["ReturnValue"];
                    notes.Add("WMI StartMode Return=" + (rv ?? "?"));
                }
            }
            catch (Exception wmiEx)
            {
                notes.Add("WMI StartMode: " + wmiEx.Message);
            }

            notes.Add("MsKeyboardFilter=Automatic (sem start forçado; reboot activa filtro CAD)");
        }
        catch (Exception ex)
        {
            notes.Add("MsKeyboardFilter auto: " + ex.Message);
        }
    }

    /// <summary>
    /// Scripts + tarefas ONSTART/ONLOGON validados em campo para manter filtro após reboot.
    /// </summary>
    private static bool RegisterPersistenceScriptsAndTasks(List<string> notes)
    {
        try
        {
            Directory.CreateDirectory(ProductPaths.Logs);
            Directory.CreateDirectory(ProductPaths.SecurityLogs);

            string bootBat = Path.Combine(ProductPaths.Logs, "force-keyboard-filter-boot.bat");
            string postPs1 = Path.Combine(ProductPaths.Logs, "post-reboot-wekf.ps1");
            string postBat = Path.Combine(ProductPaths.Logs, "post-reboot-wekf.bat");

            File.WriteAllText(bootBat, BuildForceBootBat(), Encoding.ASCII);
            File.WriteAllText(postPs1, BuildPostRebootPs1(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            // Wrapper BAT — schtasks /TR com powershell -File é frágil com aspas
            File.WriteAllText(
                postBat,
                "@echo off\r\n" +
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" +
                postPs1 + "\"\r\n",
                Encoding.ASCII);

            // ONSTART SYSTEM — reforça AUTO + reg CAD em cada boot
            RunSchtasksDelete(ForceBootTaskName);
            ProcessRunner.Run(
                "schtasks.exe",
                "/Create /TN \"" + ForceBootTaskName + "\" /SC ONSTART /RU SYSTEM /RL HIGHEST /F " +
                "/TR \"" + bootBat + "\"",
                timeoutMs: 30_000,
                operationName: "cad-boot-task");
            notes.Add("task " + ForceBootTaskName + " ONSTART");

            // ONSTART — aplica WEKF quando a classe WMI já existe (pós-feature/reboot)
            RunSchtasksDelete(PostRebootTaskName);
            ProcessRunner.Run(
                "schtasks.exe",
                "/Create /TN \"" + PostRebootTaskName + "\" /SC ONSTART /RU SYSTEM /RL HIGHEST /F " +
                "/TR \"" + postBat + "\"",
                timeoutMs: 30_000,
                operationName: "cad-post-task");
            notes.Add("task " + PostRebootTaskName + " ONSTART");
            notes.Add("scripts: " + bootBat + " ; " + postBat);
            return true;
        }
        catch (Exception ex)
        {
            notes.Add("persistência: " + ex.Message);
            return false;
        }
    }

    private static void RunSchtasksDelete(string taskName)
    {
        try
        {
            ProcessRunner.Run(
                "schtasks.exe",
                "/Delete /TN \"" + taskName + "\" /F",
                timeoutMs: 15_000,
                operationName: "cad-task-del");
        }
        catch
        {
            // ignore missing task
        }
    }

    private static string BuildForceBootBat()
    {
        // ASCII only — validado em apply-official-cad-block.ps1
        return string.Join("\r\n", new[]
        {
            "@echo off",
            "reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\MsKeyboardFilter\" /v Start /t REG_DWORD /d 2 /f >nul",
            "sc config MsKeyboardFilter start= auto >nul",
            "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows Embedded\\KeyboardFilter\" /v \"Ctrl+Alt+Del\" /t REG_SZ /d Blocked /f >nul",
            "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows Embedded\\KeyboardFilter\" /v \"Ctrl+End\" /t REG_SZ /d Allowed /f >nul",
            "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows Embedded\\KeyboardFilter\" /v \"DisableKeyboardFilterForAdministrators\" /t REG_DWORD /d 0 /f >nul",
            "powershell -NoProfile -Command \"try { $k=Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\\standardcimv2\\embedded | Where-Object { $_.Id -eq 'Ctrl+Alt+Del' }; if($k){ $k.Enabled=1; $k.Put()|Out-Null } } catch {}\"",
            ""
        });
    }

    private static string BuildPostRebootPs1()
    {
        return """
$log = "C:\TurboRama\Logs\post-reboot-wekf.log"
function L($m) { Add-Content $log ((Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " " + $m) }
L "=== post-reboot WEKF (installer) ==="
sc.exe config MsKeyboardFilter start= auto | Out-Null
try {
    $reg = "HKLM:\SYSTEM\CurrentControlSet\Services\MsKeyboardFilter"
    if (Test-Path $reg) { Set-ItemProperty -Path $reg -Name Start -Value 2 -Type DWord -Force }
} catch {}
$s = Get-Service MsKeyboardFilter -ErrorAction SilentlyContinue
L ("Service " + $s.Status + " " + $s.StartType)
try {
    $kf = "HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter"
    if (-not (Test-Path $kf)) { New-Item $kf -Force | Out-Null }
    New-ItemProperty $kf -Name "Ctrl+Alt+Del" -Value "Blocked" -PropertyType String -Force | Out-Null
    New-ItemProperty $kf -Name "Ctrl+End" -Value "Allowed" -PropertyType String -Force | Out-Null
} catch { L ("reg: " + $_.Exception.Message) }
try {
    $k = Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded |
        Where-Object { $_.Id -eq "Ctrl+Alt+Del" }
    if ($k) {
        $k.Enabled = 1
        $k.Put() | Out-Null
        L "Blocked Ctrl+Alt+Del WEKF OK"
    }
    else {
        $k2 = Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded |
            Where-Object { $_.Id -match "Ctrl\+Alt\+Del" }
        if ($k2) {
            $k2.Enabled = 1
            $k2.Put() | Out-Null
            L ("Blocked " + $k2.Id)
        }
        else {
            L "WEKF CAD key not found yet"
        }
    }
    $end = Get-WmiObject -Class WEKF_PredefinedKey -Namespace root\standardcimv2\embedded |
        Where-Object { $_.Id -eq "Ctrl+End" }
    if ($end) {
        $end.Enabled = 0
        $end.Put() | Out-Null
        L "Allowed Ctrl+End WEKF OK"
    }
}
catch {
    L ("WEKF: " + $_.Exception.Message)
}
L "=== done ==="
""";
    }

    private static void WriteApplyLog(List<string> notes, bool wekfNow)
    {
        try
        {
            Directory.CreateDirectory(ProductPaths.Logs);
            string path = Path.Combine(ProductPaths.Logs, "cad-block-installer.log");
            var sb = new StringBuilder();
            sb.AppendLine((DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + " CadBlockService.ApplySystemWide");
            sb.AppendLine("wekfNow=" + wekfNow);
            foreach (string n in notes)
            {
                sb.AppendLine("  " + n);
            }

            sb.AppendLine("NEXT: se MsKeyboardFilter não estiver Running, REBOOT.");
            sb.AppendLine("TESTE: Ctrl+Alt+Del bloqueado; Ctrl+End = menu TurboRama.");
            File.AppendAllText(path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }
}
