using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TurboRama.Core.Manifest;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;

namespace TurboRama.Core.Baseline;

public static class BaselineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetDirectory(Guid installationId) =>
        Path.Combine(ProductPaths.Backup, installationId.ToString("D"), "baseline");

    public static string GetDocumentPath(Guid installationId) =>
        Path.Combine(GetDirectory(installationId), "baseline.json");

    public static OperationResult Save(BaselineDocument document)
    {
        try
        {
            string dir = GetDirectory(document.InstallationId);
            Directory.CreateDirectory(dir);
            string path = GetDocumentPath(document.InstallationId);

            document.Sha256OfDocument = null;
            string json = JsonSerializer.Serialize(document, JsonOptions);
            document.Sha256OfDocument = BaselineHash.Sha256Text(json);
            json = JsonSerializer.Serialize(document, JsonOptions);

            File.WriteAllText(path, json, Encoding.UTF8);

            // Marcador de baseline "atual" da máquina
            string latest = Path.Combine(ProductPaths.Backup, "LATEST-INSTALLATION-ID.txt");
            File.WriteAllText(latest, document.InstallationId.ToString("D"));

            return OperationResult.Ok(
                "Baseline salvo: " + path + " (sha256=" + document.Sha256OfDocument[..12] + "…)",
                "BaselineStore.Save");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao salvar baseline: " + ex.Message,
                "BASELINE_SAVE",
                "BaselineStore.Save",
                exception: ex);
        }
    }

    public static OperationResult Load(Guid installationId, out BaselineDocument? document)
    {
        document = null;
        try
        {
            string path = GetDocumentPath(installationId);
            if (!File.Exists(path))
            {
                return OperationResult.Fail("Baseline ausente: " + path, "BASELINE_MISSING", "BaselineStore.Load");
            }

            document = JsonSerializer.Deserialize<BaselineDocument>(File.ReadAllText(path), JsonOptions);
            if (document is null)
            {
                return OperationResult.Fail("Baseline inválido.", "BASELINE_PARSE", "BaselineStore.Load");
            }

            return OperationResult.Ok(
                "Baseline carregado: " + document.RegistryValues.Count + " valores de registro.",
                "BaselineStore.Load");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao carregar baseline: " + ex.Message,
                "BASELINE_LOAD",
                "BaselineStore.Load",
                exception: ex);
        }
    }

    public static bool TryGetLatestInstallationId(out Guid installationId)
    {
        installationId = Guid.Empty;
        string latest = Path.Combine(ProductPaths.Backup, "LATEST-INSTALLATION-ID.txt");
        if (!File.Exists(latest))
        {
            return false;
        }

        return Guid.TryParse(File.ReadAllText(latest).Trim(), out installationId);
    }
}
