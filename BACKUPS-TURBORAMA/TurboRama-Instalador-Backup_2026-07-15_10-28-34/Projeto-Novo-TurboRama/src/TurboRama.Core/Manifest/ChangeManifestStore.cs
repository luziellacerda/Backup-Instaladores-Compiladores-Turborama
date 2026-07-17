using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;

namespace TurboRama.Core.Manifest;

public static class ChangeManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetPath(Guid installationId) =>
        ProductPaths.ChangeManifestFile(installationId);

    public static OperationResult Save(ChangeManifest manifest, string? path = null)
    {
        try
        {
            path ??= GetPath(manifest.InstallationId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
            return OperationResult.Ok("Manifesto salvo: " + path, "ChangeManifestStore.Save");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao salvar manifesto: " + ex.Message,
                "MANIFEST_SAVE",
                "ChangeManifestStore.Save",
                exception: ex);
        }
    }

    public static OperationResult Load(Guid installationId, out ChangeManifest? manifest, string? path = null)
    {
        manifest = null;
        try
        {
            path ??= GetPath(installationId);
            if (!File.Exists(path))
            {
                return OperationResult.Fail("Manifesto ausente: " + path, "MANIFEST_MISSING", "ChangeManifestStore.Load");
            }

            manifest = JsonSerializer.Deserialize<ChangeManifest>(File.ReadAllText(path), JsonOptions);
            if (manifest is null)
            {
                return OperationResult.Fail("Manifesto inválido.", "MANIFEST_PARSE", "ChangeManifestStore.Load");
            }

            return OperationResult.Ok("Manifesto carregado (" + manifest.Changes.Count + " itens).", "ChangeManifestStore.Load");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao carregar manifesto: " + ex.Message,
                "MANIFEST_LOAD",
                "ChangeManifestStore.Load",
                exception: ex);
        }
    }

    public static void AddChange(
        ChangeManifest manifest,
        string type,
        string target,
        string? originalValue,
        string? newValue,
        bool originalExisted,
        string stepName,
        string status = "Applied")
    {
        manifest.Changes.Add(new ChangeEntry
        {
            Type = type,
            Target = target,
            OriginalValue = originalValue,
            NewValue = newValue,
            OriginalExisted = originalExisted,
            Status = status,
            RollbackStatus = "Pending",
            StepName = stepName,
            AppliedAt = DateTimeOffset.Now
        });
    }
}

public static class BaselineHash
{
    public static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    public static string Sha256Text(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }
}
