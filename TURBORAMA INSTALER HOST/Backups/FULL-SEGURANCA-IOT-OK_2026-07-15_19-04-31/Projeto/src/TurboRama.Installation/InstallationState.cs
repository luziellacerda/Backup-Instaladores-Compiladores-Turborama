using TurboRama.Core.Steps;

namespace TurboRama.Installation;

/// <summary>
/// Estado persistido da instalação (máquina de estados — estudo §4).
/// Permite retomar ou reverter após queda de energia.
/// </summary>
public sealed class InstallationState
{
    public int SchemaVersion { get; set; } = 1;
    public Guid InstallationId { get; set; }
    public InstallationStage CurrentStage { get; set; } = InstallationStage.NotStarted;
    public List<string> CompletedStages { get; set; } = new();
    public string? FailedStage { get; set; }
    public string? LastError { get; set; }
    /// <summary>Etapa marcada IN_PROGRESS na última execução (resume).</summary>
    public string? InProgressStage { get; set; }
    public string Profile { get; set; } = "KioskBasic";
    public string ProductVersion { get; set; } = "2.0.0-alpha";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Se a última corrida morreu no meio de um step, remove o marcador IN_PROGRESS
    /// e NÃO conta o step como concluído (será refeito com Capture→Apply).
    /// </summary>
    public void NormalizeAfterCrash()
    {
        if (FailedStage is not null &&
            FailedStage.EndsWith(":IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            string step = FailedStage.Replace(":IN_PROGRESS", "", StringComparison.OrdinalIgnoreCase);
            CompletedStages.RemoveAll(s => s.Equals(step, StringComparison.OrdinalIgnoreCase));
            InProgressStage = step;
            LastError = "Retomando após interrupção em " + step;
            FailedStage = null;
            CurrentStage = InstallationStage.NotStarted;
        }
    }
}
