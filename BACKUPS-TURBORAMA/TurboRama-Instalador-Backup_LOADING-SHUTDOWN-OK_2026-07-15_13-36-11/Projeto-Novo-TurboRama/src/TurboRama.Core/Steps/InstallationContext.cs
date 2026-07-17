namespace TurboRama.Core.Steps;

/// <summary>
/// Contexto compartilhado entre etapas de uma instalação.
/// Dados de baseline e manifesto ficam em StateDirectory / BackupDirectory.
/// </summary>
public sealed class InstallationContext
{
    public required Guid InstallationId { get; init; }
    public required string ProductVersion { get; init; }
    public required string InstallDirectory { get; init; }
    public required string StateDirectory { get; init; }
    public required string BackupDirectory { get; init; }
    public required string LogsDirectory { get; init; }
    public string KioskUserName { get; init; } = "Arcade";
    /// <summary>Senha kiosk resolvida (fábrica ou override) — só em memória no pipeline.</summary>
    public string KioskPassword { get; init; } = string.Empty;
    public string FrontendExecutable { get; init; } = string.Empty;
    public InstallationProfile Profile { get; init; } = InstallationProfile.KioskBasic;
    public IDictionary<string, string> Properties { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string InstallationBackupRoot =>
        Path.Combine(BackupDirectory, InstallationId.ToString("D"));
}
