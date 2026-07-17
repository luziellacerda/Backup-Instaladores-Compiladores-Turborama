namespace TurboRama.Core.Manifest;

/// <summary>
/// Manifesto legível de todas as alterações de uma instalação.
/// Usado pela UI de auditoria e pelo rollback.
/// </summary>
public sealed class ChangeManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid InstallationId { get; set; }
    public string ProductVersion { get; set; } = "2.0.0";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Profile { get; set; } = "KioskBasic";
    public string MachineName { get; set; } = Environment.MachineName;
    public List<ChangeEntry> Changes { get; set; } = new();
}

public sealed class ChangeEntry
{
    public string Type { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? OriginalValue { get; set; }
    public string? NewValue { get; set; }
    public bool OriginalExisted { get; set; }
    public string Status { get; set; } = "Pending";
    public string RollbackStatus { get; set; } = "Pending";
    public string? StepName { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}
