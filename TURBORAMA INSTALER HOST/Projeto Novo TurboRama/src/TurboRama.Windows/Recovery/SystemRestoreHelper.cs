using TurboRama.Core.Results;
using TurboRama.Windows.Exec;

namespace TurboRama.Windows.Recovery;

/// <summary>
/// Tenta criar ponto de restauração antes de mudanças (proposta §5) — best effort, não bloqueia.
/// </summary>
public static class SystemRestoreHelper
{
    public static OperationResult TryCreateRestorePoint(string description = "TurboRama Secure pre-install")
    {
        try
        {
            // Checkpoint-Computer (PowerShell) — falha silenciosa em edições sem SR
            string desc = description.Replace("'", "''");
            // Timeout curto: se SR desabilitado, não segurar o instalador.
            OperationResult r = ProcessRunner.Run(
                "powershell.exe",
                "-NoProfile -Command \"try { Checkpoint-Computer -Description '" + desc +
                "' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK' } catch { $_.Exception.Message }\"",
                timeoutMs: 20_000,
                operationName: "restore-point");

            if (r.Success && (r.Message?.Contains("OK", StringComparison.OrdinalIgnoreCase) == true))
            {
                return OperationResult.Ok("Ponto de restauração criado: " + description, "SystemRestore");
            }

            return OperationResult.Ok(
                "Ponto de restauração não criado (edição/política/serviço SR): " + (r.Message ?? ""),
                "SystemRestore",
                currentState: "UnavailableOrSkipped");
        }
        catch (Exception ex)
        {
            return OperationResult.Ok("Restore point skip: " + ex.Message, "SystemRestore", currentState: "Error");
        }
    }

    public static OperationResult ProbeAvailability()
    {
        OperationResult r = ProcessRunner.Run(
            "powershell.exe",
            "-NoProfile -Command \"Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Select-Object -First 1 | Out-String\"",
            timeoutMs: 15_000,
            operationName: "restore-probe");
        if (r.Success && !string.IsNullOrWhiteSpace(r.Message) && r.Message.Length > 10)
        {
            return OperationResult.Ok("System Restore disponível (há pontos ou serviço responde).", "SystemRestore.Probe");
        }

        return OperationResult.Ok("System Restore não confirmado (comum se desabilitado).", "SystemRestore.Probe", currentState: "Unknown");
    }
}
