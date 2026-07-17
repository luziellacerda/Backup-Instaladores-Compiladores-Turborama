using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Security.Policies;
using TurboRama.Windows.Logon;
using TurboRama.Windows.Security;

namespace TurboRama.Installation.Steps;

public sealed class ApplyKioskPoliciesStep : IInstallationStep
{
    public string Name => "ApplyKioskPolicies";
    public int Order => 70;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(OperationResult.Ok("Políticas serão capturadas no Apply.", Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string backup = Path.Combine(context.InstallationBackupRoot, "kiosk-policies.json");
        OperationResult r = KioskPolicyService.ApplyForUser(context.KioskUserName, backup, out _);
        context.Properties["KioskPoliciesBackup"] = backup;

        // Esconde flash de logon Windows no AutoLogon (não remove bolinhas de boot)
        OperationResult quiet = LogonUiQuietService.ApplyQuietAutoLogonUi();

        // Ctrl+Alt+Del: esvaziar/bloquear — menu útil = Ctrl+End (agente no Launcher)
        OperationResult cad = CadBlockService.ApplySystemWide();
        string agentNote = RegisterSecurityAgent();

        string msg = r.Message + " | " + quiet.Message + " | " + cad.Message + " | " + agentNote;
        if (!r.Success)
        {
            return Task.FromResult(r);
        }

        return Task.FromResult(OperationResult.Ok(msg, Name));
    }

    private static string RegisterSecurityAgent()
    {
        try
        {
            string launcher = Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
            if (!File.Exists(launcher))
            {
                return "SecurityAgent: Launcher ainda ausente";
            }

            string cmd = "\"" + launcher + "\" --security-agent";
            using (var lm = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                lm?.SetValue("TurboRamaSecurityAgent", cmd);
            }

            using (var cu = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                cu?.SetValue("TurboRamaSecurityAgent", cmd);
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/Delete /TN \"TurboRamaSecurityAgent\" /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit(8000);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments =
                        "/Create /TN \"TurboRamaSecurityAgent\" /SC ONLOGON /RL LIMITED /F " +
                        "/TR \"\\\"" + launcher + "\\\" --security-agent\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit(15000);
            }
            catch
            {
                // ignore
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = launcher,
                    Arguments = "--security-agent",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(launcher) ?? ""
                });
            }
            catch
            {
                // ignore
            }

            return "SecurityAgent Ctrl+End registado";
        }
        catch (Exception ex)
        {
            return "SecurityAgent: " + ex.Message;
        }
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        if (!context.Properties.TryGetValue("KioskPoliciesBackup", out string? path) || !File.Exists(path))
        {
            return Task.FromResult(OperationResult.Fail("Backup de políticas ausente.", "POL_VAL", Name));
        }

        return Task.FromResult(OperationResult.Ok("Políticas kiosk aplicadas (backup OK).", Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        if (!context.Properties.TryGetValue("KioskPoliciesBackup", out string? path))
        {
            path = Path.Combine(context.InstallationBackupRoot, "kiosk-policies.json");
        }

        return Task.FromResult(KioskPolicyService.RestoreFromBackup(context.KioskUserName, path));
    }
}
