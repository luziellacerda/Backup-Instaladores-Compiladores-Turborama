namespace TurboRama.Core.Baseline;

/// <summary>
/// Baseline completo capturado antes de alterar o Windows (estudo §6).
/// Rollback restaura estes valores — nunca "padrões imaginários".
/// </summary>
public sealed class BaselineDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid InstallationId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string MachineName { get; set; } = Environment.MachineName;
    public string CapturedBy { get; set; } = Environment.UserName;
    public string WindowsVersion { get; set; } = Environment.OSVersion.VersionString;
    public string ProductVersion { get; set; } = "2.0.0-alpha";

    public List<RegistryValueSnapshot> RegistryValues { get; set; } = new();
    public BcdSnapshot? Bcd { get; set; }
    public List<AclSnapshot> Acls { get; set; } = new();
    public List<ServiceSnapshot> Services { get; set; } = new();
    public List<OptionalFeatureSnapshot> OptionalFeatures { get; set; } = new();
    public string? Sha256OfDocument { get; set; }
    /// <summary>Notas extras (tarefas agendadas, etc.).</summary>
    public string? Notes { get; set; }
}

public sealed class RegistryValueSnapshot
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RegistryView { get; set; } = "Registry64";
    public bool Existed { get; set; }
    public string? Kind { get; set; }
    public string? Value { get; set; }
}

public sealed class BcdSnapshot
{
    public string ExportFileName { get; set; } = "bcd-backup";
    public string? ExportRelativePath { get; set; }
    public string? EnumTextRelativePath { get; set; }
    public string? Sha256 { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public bool ExportSucceeded { get; set; }
    public string? Message { get; set; }
}

public sealed class AclSnapshot
{
    public string TargetPath { get; set; } = string.Empty;
    public string? IcaclsRelativePath { get; set; }
    public bool Succeeded { get; set; }
    public string? Message { get; set; }
    public string? Owner { get; set; }
}

public sealed class ServiceSnapshot
{
    public string ServiceName { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public string? StartType { get; set; }
    public string? State { get; set; }
    public string? BinaryPath { get; set; }
    public string? Account { get; set; }
    public string? RawQuery { get; set; }
}

public sealed class OptionalFeatureSnapshot
{
    public string FeatureName { get; set; } = string.Empty;
    public string State { get; set; } = "Unknown";
    public bool Present { get; set; }
}
