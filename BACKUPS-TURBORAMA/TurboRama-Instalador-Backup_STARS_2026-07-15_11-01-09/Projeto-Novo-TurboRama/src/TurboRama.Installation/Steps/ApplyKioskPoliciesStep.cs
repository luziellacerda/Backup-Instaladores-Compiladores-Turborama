using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Security.Policies;
using TurboRama.Windows.Logon;

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
        string msg = r.Message + " | " + quiet.Message;
        if (!r.Success)
        {
            return Task.FromResult(r);
        }

        return Task.FromResult(OperationResult.Ok(msg, Name));
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
