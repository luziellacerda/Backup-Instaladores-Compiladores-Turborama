using TurboRama.Core.Results;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Features;

namespace TurboRama.Windows.Shell;

/// <summary>
/// Escolha segura de shell (estudo §11):
/// 1) Shell Launcher / Assigned Access se recurso existir (sondagem)
/// 2) shell por usuário (hive) — caminho principal implementado
/// 3) HKLM global — NUNCA automático aqui
/// </summary>
public static class ShellStrategyService
{
    public sealed class StrategyResult
    {
        public string Mode { get; init; } = "UserHive";
        public string Message { get; init; } = string.Empty;
        public bool UsedOfficialLockdown { get; init; }
    }

    public static StrategyResult ProbeAndDescribe()
    {
        var lockdown = OptionalFeatureSnapshotService.CaptureOne("Client-DeviceLockdown");
        var bootExp = OptionalFeatureSnapshotService.CaptureOne("Client-EmbeddedBootExp");
        var logon = OptionalFeatureSnapshotService.CaptureOne("Client-EmbeddedLogon");

        bool anyPresent = lockdown.Present || bootExp.Present || logon.Present;
        if (!anyPresent)
        {
            return new StrategyResult
            {
                Mode = "UserHive",
                Message = "Device Lockdown/Embedded não presentes — usar shell por hive do usuário (seguro).",
                UsedOfficialLockdown = false
            };
        }

        // Não ativamos Shell Launcher automaticamente (risco alto). Documentamos e preferimos hive.
        return new StrategyResult
        {
            Mode = "UserHivePreferred",
            Message =
                "Edição com recursos Embedded/DeviceLockdown detectados. " +
                "Shell Launcher/Assigned Access NÃO são forçados (default seguro = hive por usuário). " +
                "Features: lockdown=" + lockdown.Present + " bootExp=" + bootExp.Present + " logon=" + logon.Present,
            UsedOfficialLockdown = false
        };
    }

    /// <summary>
    /// Aplica shell do kiosk de forma segura: sempre hive por usuário (prioridade 3 da proposta como padrão operacional).
    /// Shell Launcher oficial exige tooling adicional e aceite explícito — não é default.
    /// </summary>
    public static OperationResult ApplyKioskShellSafe(string userName, string launcherExe, out Core.Baseline.RegistryValueSnapshot captured)
    {
        StrategyResult probe = ProbeAndDescribe();
        OperationResult set = UserShellService.SetUserShell(userName, "\"" + launcherExe + "\"", out captured);
        if (!set.Success)
        {
            return set;
        }

        return OperationResult.Ok(
            set.Message + " | estratégia=" + probe.Mode + " | " + probe.Message,
            "ShellStrategy",
            previousState: captured.Value,
            currentState: launcherExe);
    }
}
