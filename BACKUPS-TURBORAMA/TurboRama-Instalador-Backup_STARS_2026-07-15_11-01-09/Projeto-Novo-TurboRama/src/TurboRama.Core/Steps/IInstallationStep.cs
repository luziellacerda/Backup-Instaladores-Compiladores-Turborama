using TurboRama.Core.Results;

namespace TurboRama.Core.Steps;

/// <summary>
/// Contrato de etapa transacional.
/// Ordem obrigatória: Capture → Apply → Validate; Rollback restaura o capturado.
/// </summary>
public interface IInstallationStep
{
    /// <summary>Nome estável da etapa (persistido no state).</summary>
    string Name { get; }

    /// <summary>Ordem na instalação (menor primeiro). Rollback usa ordem inversa.</summary>
    int Order { get; }

    Task<OperationResult> CaptureAsync(
        InstallationContext context,
        CancellationToken cancellationToken);

    Task<OperationResult> ApplyAsync(
        InstallationContext context,
        CancellationToken cancellationToken);

    Task<OperationResult> ValidateAsync(
        InstallationContext context,
        CancellationToken cancellationToken);

    Task<OperationResult> RollbackAsync(
        InstallationContext context,
        CancellationToken cancellationToken);
}
