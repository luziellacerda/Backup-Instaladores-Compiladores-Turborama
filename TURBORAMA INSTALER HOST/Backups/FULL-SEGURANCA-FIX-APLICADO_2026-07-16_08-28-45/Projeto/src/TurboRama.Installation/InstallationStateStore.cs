using System.Text.Json;
using System.Text.Json.Serialization;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;

namespace TurboRama.Installation;

public static class InstallationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static OperationResult Save(InstallationState state, string? path = null)
    {
        try
        {
            path ??= ProductPaths.InstallationStateFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            state.UpdatedAt = DateTimeOffset.Now;
            File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
            return OperationResult.Ok("Estado salvo: " + state.CurrentStage, "InstallationStateStore.Save");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao salvar estado: " + ex.Message,
                "STATE_SAVE",
                "InstallationStateStore.Save",
                exception: ex);
        }
    }

    public static OperationResult Load(out InstallationState state, string? path = null)
    {
        state = new InstallationState();
        try
        {
            path ??= ProductPaths.InstallationStateFile;
            if (!File.Exists(path))
            {
                return OperationResult.Ok("Sem estado prévio.", "InstallationStateStore.Load");
            }

            InstallationState? loaded = JsonSerializer.Deserialize<InstallationState>(
                File.ReadAllText(path), JsonOptions);
            if (loaded is null)
            {
                return OperationResult.Fail("Estado inválido.", "STATE_PARSE", "InstallationStateStore.Load");
            }

            state = loaded;
            return OperationResult.Ok(
                "Estado: " + state.CurrentStage + " id=" + state.InstallationId.ToString("D"),
                "InstallationStateStore.Load");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao carregar estado: " + ex.Message,
                "STATE_LOAD",
                "InstallationStateStore.Load",
                exception: ex);
        }
    }

    public static InstallationState CreateNew(Guid installationId, string profile, string productVersion) =>
        new()
        {
            SchemaVersion = 1,
            InstallationId = installationId,
            CurrentStage = InstallationStage.NotStarted,
            CompletedStages = new List<string>(),
            Profile = profile,
            ProductVersion = productVersion,
            UpdatedAt = DateTimeOffset.Now
        };
}
