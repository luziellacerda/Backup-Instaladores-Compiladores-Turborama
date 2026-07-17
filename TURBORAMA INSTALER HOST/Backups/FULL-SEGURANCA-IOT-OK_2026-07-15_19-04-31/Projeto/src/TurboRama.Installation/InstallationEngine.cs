using TurboRama.Core.Logging;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;

namespace TurboRama.Installation;

/// <summary>
/// Orquestra etapas: Capture → Apply → Validate; em falha, Rollback na ordem inversa.
/// </summary>
public sealed class InstallationEngine
{
    private readonly IReadOnlyList<IInstallationStep> _steps;
    private readonly ITurboRamaLogger _logger;

    public InstallationEngine(IEnumerable<IInstallationStep> steps, ITurboRamaLogger logger)
    {
        _steps = steps.OrderBy(s => s.Order).ToList();
        _logger = logger;
    }

    public async Task<OperationResult> RunAsync(
        InstallationContext context,
        InstallationState state,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("Installer", "Iniciando instalação " + context.InstallationId.ToString("D"), state.CurrentStage.ToString());

        var applied = new Stack<IInstallationStep>();

        foreach (IInstallationStep step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (state.CompletedStages.Contains(step.Name, StringComparer.OrdinalIgnoreCase))
            {
                _logger.Info("Installer", "Etapa já concluída, pulando: " + step.Name, step.Name);
                continue;
            }

            // Resume seguro: marca etapa em andamento (queda de energia → FailedStage conhecido)
            state.CurrentStage = InstallationStage.BaselineCaptured; // progresso genérico
            if (string.Equals(step.Name, "CreateKioskAccount", StringComparison.OrdinalIgnoreCase))
            {
                state.CurrentStage = InstallationStage.KioskUserCreated;
            }
            else if (step.Name.Contains("Shell", StringComparison.OrdinalIgnoreCase))
            {
                state.CurrentStage = InstallationStage.ShellConfigured;
            }
            else if (step.Name.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
                     step.Name.Contains("Watchdog", StringComparison.OrdinalIgnoreCase))
            {
                state.CurrentStage = InstallationStage.WatchdogInstalled;
            }

            state.FailedStage = step.Name + ":IN_PROGRESS";
            state.LastError = null;
            state.UpdatedAt = DateTimeOffset.Now;
            InstallationStateStore.Save(state);
            _logger.Info("Installer", "Iniciando etapa (retomável): " + step.Name, step.Name);

            OperationResult capture = await step.CaptureAsync(context, cancellationToken).ConfigureAwait(false);
            _logger.Log(
                capture.Success ? LogLevel.Info : LogLevel.Error,
                "Installer",
                capture.Message,
                step.Name,
                "Capture",
                capture.ErrorCode,
                capture.Duration);

            if (!capture.Success)
            {
                return FailAndMark(state, step.Name, capture);
            }

            OperationResult apply = await step.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
            _logger.Log(
                apply.Success ? LogLevel.Info : LogLevel.Error,
                "Installer",
                apply.Message,
                step.Name,
                "Apply",
                apply.ErrorCode,
                apply.Duration);

            if (!apply.Success)
            {
                await RollbackAppliedAsync(applied, context, cancellationToken).ConfigureAwait(false);
                return FailAndMark(state, step.Name, apply);
            }

            applied.Push(step);

            OperationResult validate = await step.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            _logger.Log(
                validate.Success ? LogLevel.Info : LogLevel.Error,
                "Installer",
                validate.Message,
                step.Name,
                "Validate",
                validate.ErrorCode,
                validate.Duration);

            if (!validate.Success)
            {
                await RollbackAppliedAsync(applied, context, cancellationToken).ConfigureAwait(false);
                return FailAndMark(state, step.Name, validate);
            }

            if (!state.CompletedStages.Contains(step.Name, StringComparer.OrdinalIgnoreCase))
            {
                state.CompletedStages.Add(step.Name);
            }

            InstallationStateStore.Save(state);
        }

        state.CurrentStage = InstallationStage.Installed;
        state.FailedStage = null;
        state.LastError = null;
        InstallationStateStore.Save(state);

        _logger.Info("Installer", "Instalação concluída.", InstallationStage.Installed.ToString());
        return OperationResult.Ok("Instalação concluída.", "InstallationEngine.Run");
    }

    private async Task RollbackAppliedAsync(
        Stack<IInstallationStep> applied,
        InstallationContext context,
        CancellationToken cancellationToken)
    {
        while (applied.Count > 0)
        {
            IInstallationStep step = applied.Pop();
            try
            {
                OperationResult rb = await step.RollbackAsync(context, cancellationToken).ConfigureAwait(false);
                _logger.Log(
                    rb.Success ? LogLevel.Info : LogLevel.Warning,
                    "Installer",
                    rb.Message,
                    step.Name,
                    "Rollback",
                    rb.ErrorCode,
                    rb.Duration);
            }
            catch (Exception ex)
            {
                _logger.Error("Installer", "Exceção no rollback de " + step.Name + ": " + ex.Message, step.Name, "RB_EX");
            }
        }
    }

    private static OperationResult FailAndMark(InstallationState state, string stepName, OperationResult failure)
    {
        state.CurrentStage = InstallationStage.Failed;
        state.FailedStage = stepName;
        state.LastError = failure.Message;
        InstallationStateStore.Save(state);
        return failure;
    }
}
