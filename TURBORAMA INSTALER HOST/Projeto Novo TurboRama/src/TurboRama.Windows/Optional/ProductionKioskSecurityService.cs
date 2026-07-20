using System.Text;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Services;
using WinReg = Microsoft.Win32;

namespace TurboRama.Windows.Optional;

/// <summary>
/// Aplica o mesmo lockdown de Windows deste PC de referência (produção):
/// DeviceLockdown + Keyboard Filter + políticas CAD + SecurityAgent + keep-alive.
/// Não instala jogos/frontend — só o SO em modo kiosk trancado.
/// </summary>
public static class ProductionKioskSecurityService
{
    /// <summary>
    /// Espelha INSTALAR-SEGURANCA.bat (sem prompt de reboot).
    /// Best-effort: se a edição não tiver IoT Keyboard Filter, ainda aplica políticas + agent.
    /// </summary>
    public static OperationResult Apply(string? launcherExe = null)
    {
        var notes = new List<string>();
        string exe = launcherExe
            ?? Path.Combine(ProductPaths.Root, "App", "Launcher", "TurboRama.Launcher.exe");

        if (!File.Exists(exe))
        {
            return OperationResult.Fail(
                "Launcher não encontrado para SecurityAgent: " + exe,
                "SEC_NO_LAUNCHER",
                "ProductionSecurity.Apply");
        }

        // 1) Features IoT (pode falhar em Pro/Home — continua)
        try
        {
            OperationResult dism = ProcessRunner.Run(
                "dism.exe",
                "/Online /Enable-Feature /FeatureName:Client-DeviceLockdown /FeatureName:Client-KeyboardFilter /All /NoRestart",
                timeoutMs: 180_000,
                operationName: "dism-lockdown");
            notes.Add("DISM: " + (dism.Success ? "OK" : dism.Message));
        }
        catch (Exception ex)
        {
            notes.Add("DISM: " + ex.Message);
        }

        // 2) Keyboard Filter service AUTO (sem sc start — igual IoT LTSC)
        OperationResult kb = KeyboardFilterModuleService.Enable();
        notes.Add("KbFilter: " + kb.Message);

        // 3) Registry Keyboard Filter shortcuts (como neste PC)
        try
        {
            using WinReg.RegistryKey key = WinReg.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter", true)!;
            key.SetValue("Ctrl+Alt+Del", "Blocked", WinReg.RegistryValueKind.String);
            key.SetValue("Ctrl+End", "Allowed", WinReg.RegistryValueKind.String);
            key.SetValue("DisableKeyboardFilterForAdministrators", 0, WinReg.RegistryValueKind.DWord);
            foreach (string blocked in new[]
                     {
                         "Windows", "Win+L", "Alt+Tab", "Alt+F4", "Ctrl+Esc", "Shift+Ctrl+Esc"
                     })
            {
                try { key.SetValue(blocked, "Blocked", WinReg.RegistryValueKind.String); }
                catch { /* ignore */ }
            }

            notes.Add("KeyboardFilter.reg: OK");
        }
        catch (Exception ex)
        {
            notes.Add("KeyboardFilter.reg: " + ex.Message);
        }

        // 4) Políticas CAD / shell (iguais a este PC)
        try
        {
            using WinReg.RegistryKey sys = WinReg.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true)!;
            sys.SetValue("DisableTaskMgr", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("DisableChangePassword", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("DisableLockWorkstation", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("HideFastUserSwitching", 1, WinReg.RegistryValueKind.DWord);

            using WinReg.RegistryKey exp = WinReg.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true)!;
            exp.SetValue("NoLogoff", 1, WinReg.RegistryValueKind.DWord);

            notes.Add("Policies CAD: OK");
        }
        catch (Exception ex)
        {
            notes.Add("Policies: " + ex.Message);
        }

        // 5) WEKF WMI (só após feature/reboot; best-effort)
        try
        {
            ProcessRunner.Run(
                "powershell.exe",
                "-NoProfile -Command \"try { Get-WmiObject -Namespace root\\standardcimv2\\embedded -Class WEKF_PredefinedKey | ForEach-Object { if($_.Id -eq 'Ctrl+Alt+Del'){ $_.Enabled=$true; $_.Put()|Out-Null }; if($_.Id -match 'Ctrl\\+Esc|Win\\+L|Alt\\+Tab|Alt\\+F4|Shift\\+Ctrl\\+Esc|Windows'){ $_.Enabled=$true; $_.Put()|Out-Null } } } catch { }\"",
                timeoutMs: 30_000,
                operationName: "wekf-wmi");
            notes.Add("WEKF: attempted");
        }
        catch
        {
            notes.Add("WEKF: skip");
        }

        // 6) SecurityAgent Run + tasks + force KF on boot
        string cmd = "\"" + exe + "\" --security-agent";
        try
        {
            using WinReg.RegistryKey run = WinReg.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)!;
            run.SetValue("TurboRamaSecurityAgent", cmd, WinReg.RegistryValueKind.String);
            notes.Add("HKLM Run SecurityAgent: OK");
        }
        catch (Exception ex)
        {
            notes.Add("HKLM Run: " + ex.Message);
        }

        // schtasks: TR via arquivo .bat evita escape quebrado de aspas no ProcessRunner
        try
        {
            string logs = Path.Combine(ProductPaths.Root, "Logs");
            Directory.CreateDirectory(logs);
            string agentBat = Path.Combine(logs, "run-security-agent.bat");
            File.WriteAllText(
                agentBat,
                "@echo off\r\nstart \"\" \"" + exe + "\" --security-agent\r\n",
                Encoding.ASCII);

            ProcessRunner.Run("schtasks.exe", "/Delete /TN \"TurboRamaSecurityAgent\" /F", operationName: "sch-del-sa");
            ProcessRunner.Run(
                "schtasks.exe",
                "/Create /TN \"TurboRamaSecurityAgent\" /SC ONLOGON /RL LIMITED /F /TR \"" + agentBat + "\"",
                operationName: "sch-sa");
            ProcessRunner.Run("schtasks.exe", "/Delete /TN \"TurboRamaSecurityAgentKeepAlive\" /F", operationName: "sch-del-ka");
            ProcessRunner.Run(
                "schtasks.exe",
                "/Create /TN \"TurboRamaSecurityAgentKeepAlive\" /SC MINUTE /MO 2 /RL LIMITED /F /TR \"" + agentBat + "\"",
                operationName: "sch-ka");
            notes.Add("SecurityAgent tasks: OK (" + agentBat + ")");
        }
        catch (Exception ex)
        {
            notes.Add("SecurityAgent tasks: " + ex.Message);
        }

        // Boot reinforce Keyboard Filter
        try
        {
            string logs = Path.Combine(ProductPaths.Root, "Logs");
            Directory.CreateDirectory(logs);
            string bootBat = Path.Combine(logs, "force-keyboard-filter-boot.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\MsKeyboardFilter\" /v Start /t REG_DWORD /d 2 /f >nul");
            sb.AppendLine("sc config MsKeyboardFilter start= auto >nul");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows Embedded\\KeyboardFilter\" /v \"Ctrl+Alt+Del\" /t REG_SZ /d Blocked /f >nul");
            File.WriteAllText(bootBat, sb.ToString(), Encoding.ASCII);

            ProcessRunner.Run("schtasks.exe", "/Delete /TN \"TurboRamaForceKeyboardFilter\" /F", operationName: "sch-del-kf");
            ProcessRunner.Run(
                "schtasks.exe",
                "/Create /TN \"TurboRamaForceKeyboardFilter\" /SC ONSTART /RU SYSTEM /RL HIGHEST /F /TR \"" + bootBat + "\"",
                operationName: "sch-kf-boot");
            notes.Add("ForceKF boot task: OK");
        }
        catch (Exception ex)
        {
            notes.Add("ForceKF: " + ex.Message);
        }

        // Status file
        try
        {
            string statusPath = Path.Combine(ProductPaths.Root, "Logs", "SEGURANCA-STATUS.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(statusPath)!);
            File.WriteAllText(
                statusPath,
                "APLICADO em " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                "========================================" + Environment.NewLine +
                "Fonte: ProductionKioskSecurityService (install-full)" + Environment.NewLine +
                string.Join(Environment.NewLine, notes) + Environment.NewLine +
                Environment.NewLine +
                "Ctrl+Alt+Del : Blocked (Keyboard Filter — após reboot)" + Environment.NewLine +
                "Ctrl+End     : Allowed (menu TurboRama)" + Environment.NewLine +
                "Agent        : Run + task logon + keep-alive 2min" + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
            /* ignore */
        }

        var snap = ServiceSnapshotService.CaptureOne("MsKeyboardFilter");
        string msg =
            "Segurança Windows de produção aplicada (igual PC referência).\n" +
            string.Join(" | ", notes) + "\n" +
            "MsKeyboardFilter: " + (snap.Exists ? (snap.StartType + "/" + snap.State) : "ausente (edição sem IoT — políticas+agent OK)");

        // Não falha o install-full se só faltar feature IoT: kiosk ainda é utilizável
        return OperationResult.Ok(msg, "ProductionSecurity.Apply");
    }
}
