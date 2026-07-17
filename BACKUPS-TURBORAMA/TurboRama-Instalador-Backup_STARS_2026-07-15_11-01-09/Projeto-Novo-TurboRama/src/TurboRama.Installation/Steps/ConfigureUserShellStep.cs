using System.Text.Json;
using TurboRama.Core.Baseline;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Windows.Shell;

namespace TurboRama.Installation.Steps;

public sealed class ConfigureUserShellStep : IInstallationStep
{
    public string Name => "ConfigureUserShell";
    public int Order => 50;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        // Capture real happens inside SetUserShell; pré-marca
        return Task.FromResult(OperationResult.Ok("Pronto para shell por usuário.", Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        // Estratégia §11: sonda Embedded/DeviceLockdown; aplica shell por hive (seguro, não HKLM).
        string launcher = Path.Combine(Core.Paths.ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
        OperationResult set = ShellStrategyService.ApplyKioskShellSafe(
            context.KioskUserName,
            launcher,
            out RegistryValueSnapshot captured);
        string snapPath = Path.Combine(context.InstallationBackupRoot, "user-shell.json");
        Directory.CreateDirectory(context.InstallationBackupRoot);
        File.WriteAllText(snapPath, JsonSerializer.Serialize(captured, new JsonSerializerOptions { WriteIndented = true }));
        context.Properties["UserShellSnapshot"] = snapPath;

        if (!set.Success)
        {
            return Task.FromResult(set);
        }

        return Task.FromResult(OperationResult.Ok(set.Message, Name, previousState: captured.Value, currentState: launcher));
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        if (!context.Properties.TryGetValue("UserShellSnapshot", out string? path) || !File.Exists(path))
        {
            return Task.FromResult(OperationResult.Fail("Snapshot de shell ausente.", "SHELL_VAL", Name));
        }

        string launcher = Path.Combine(Core.Paths.ProductPaths.AppLauncher, "TurboRama.Launcher.exe");
        if (!File.Exists(launcher))
        {
            return Task.FromResult(OperationResult.Fail("Launcher ausente: " + launcher, "SHELL_VAL_LAUNCHER", Name));
        }

        return Task.FromResult(OperationResult.Ok("Shell por usuário configurado (snapshot OK).", Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        if (!context.Properties.TryGetValue("UserShellSnapshot", out string? path) || !File.Exists(path))
        {
            return Task.FromResult(OperationResult.Ok("Sem snapshot de shell.", Name));
        }

        RegistryValueSnapshot? snap = JsonSerializer.Deserialize<RegistryValueSnapshot>(File.ReadAllText(path));
        if (snap is null)
        {
            return Task.FromResult(OperationResult.Fail("Snapshot shell inválido.", "SHELL_RB", Name));
        }

        return Task.FromResult(UserShellService.RestoreUserShell(context.KioskUserName, snap));
    }
}
