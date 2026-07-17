using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Security.Policies;

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
        return Task.FromResult(r);
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
