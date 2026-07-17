using TurboRama.Core.Logging;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Installation;

namespace TurboRama.Rollback;

/// <summary>
/// Executa rollback na ordem inversa das etapas aplicadas.
/// Rollback = restaurar valores capturados, não "padrões imaginários" do Windows.
/// </summary>
public sealed class RollbackService
{
    private readonly IReadOnlyList<IInstallationStep> _steps;
    private readonly ITurboRamaLogger _logger;

    public RollbackService(IEnumerable<IInstallationStep> steps, ITurboRamaLogger logger)
    {
        _steps = steps.OrderByDescending(s => s.Order).ToList();
        _logger = logger;
    }

    public async Task<OperationResult> RollbackAllAsync(
        InstallationContext context,
        InstallationState state,
        CancellationToken cancellationToken = default)
    {
        state.CurrentStage = InstallationStage.RollingBack;
        InstallationStateStore.Save(state);

        _logger.Info("Rollback", "Iniciando rollback completo " + context.InstallationId.ToString("D"));

        var failures = new List<string>();

        foreach (IInstallationStep step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Só reverte etapas que chegaram a ser concluídas (ou todas se Failed).
            bool shouldRun = state.CompletedStages.Count == 0
                || state.CompletedStages.Contains(step.Name, StringComparer.OrdinalIgnoreCase)
                || string.Equals(state.FailedStage, step.Name, StringComparison.OrdinalIgnoreCase);

            if (!shouldRun)
            {
                continue;
            }

            try
            {
                OperationResult result = await step.RollbackAsync(context, cancellationToken).ConfigureAwait(false);
                _logger.Log(
                    result.Success ? LogLevel.Info : LogLevel.Warning,
                    "Rollback",
                    result.Message,
                    step.Name,
                    "Rollback",
                    result.ErrorCode,
                    result.Duration);

                if (!result.Success)
                {
                    failures.Add(step.Name + ": " + result.Message);
                }
                else
                {
                    state.CompletedStages.RemoveAll(s =>
                        string.Equals(s, step.Name, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex)
            {
                failures.Add(step.Name + ": " + ex.Message);
                _logger.Error("Rollback", ex.Message, step.Name, "RB_EX");
            }
        }

        if (failures.Count > 0)
        {
            state.CurrentStage = InstallationStage.Failed;
            state.LastError = string.Join(" | ", failures);
            InstallationStateStore.Save(state);
            return OperationResult.Fail(
                "Rollback com diferenças remanescentes: " + state.LastError,
                "RB_PARTIAL",
                "RollbackService.RollbackAll",
                canRollback: false);
        }

        state.CurrentStage = InstallationStage.RolledBack;
        state.FailedStage = null;
        state.LastError = null;
        InstallationStateStore.Save(state);
        _logger.Info("Rollback", "Rollback concluído.", InstallationStage.RolledBack.ToString());
        return OperationResult.Ok("Rollback concluído.", "RollbackService.RollbackAll");
    }
}
