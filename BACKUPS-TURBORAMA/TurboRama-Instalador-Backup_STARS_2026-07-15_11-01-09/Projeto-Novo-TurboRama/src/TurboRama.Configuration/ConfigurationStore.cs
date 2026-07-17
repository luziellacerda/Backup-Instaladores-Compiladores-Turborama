using System.Text.Json;
using System.Text.Json.Serialization;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;

namespace TurboRama.Configuration;

public static class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ProductConfiguration CreateDefault() => new()
    {
        SchemaVersion = 1,
        InstallationId = Guid.NewGuid(),
        KioskUser = FactoryDefaults.KioskUserName,
        InstallDirectory = ProductPaths.Root,
        FrontendExecutable = Path.Combine(ProductPaths.Frontend, "Frontend.exe"),
        Profile = "KioskBasic",
        EnableAutoLogon = true,
        EnableKeyboardFilter = false,
        EnableUwf = false,
        EnableBootBranding = false,
        EnableLauncherKeyboardHook = false,
        EnableLauncherTechMenu = true,
        // Senha efetiva vem de FactoryDefaults; não grava em JSON por padrão
        KioskPassword = null,
        Watchdog = new WatchdogOptions
        {
            Enabled = true,
            RestartDelaySeconds = 5,
            MaximumRestarts = 5
        },
        ProductVersion = "2.0.0-alpha"
    };

    public static OperationResult Save(ProductConfiguration config, string? path = null)
    {
        try
        {
            path ??= ProductPaths.ConfigFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Não persistir senha em texto no turborama.json (fica em DPAPI + FactoryDefaults no código)
            string? hold = config.KioskPassword;
            config.KioskPassword = null;
            string json = JsonSerializer.Serialize(config, JsonOptions);
            config.KioskPassword = hold;
            File.WriteAllText(path, json);
            return OperationResult.Ok("Configuração salva em " + path, "ConfigurationStore.Save");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao salvar configuração: " + ex.Message,
                "CFG_SAVE",
                "ConfigurationStore.Save",
                exception: ex);
        }
    }

    public static OperationResult Load(out ProductConfiguration config, string? path = null)
    {
        config = CreateDefault();
        try
        {
            path ??= ProductPaths.ConfigFile;
            if (!File.Exists(path))
            {
                return OperationResult.Ok("Config padrão (arquivo ausente): " + path, "ConfigurationStore.Load");
            }

            string json = File.ReadAllText(path);
            ProductConfiguration? loaded = JsonSerializer.Deserialize<ProductConfiguration>(json, JsonOptions);
            if (loaded is null)
            {
                return OperationResult.Fail(
                    "JSON de configuração inválido.",
                    "CFG_PARSE",
                    "ConfigurationStore.Load");
            }

            if (loaded.SchemaVersion < 1)
            {
                return OperationResult.Fail(
                    "schemaVersion não suportado: " + loaded.SchemaVersion,
                    "CFG_VERSION",
                    "ConfigurationStore.Load");
            }

            config = loaded;
            return OperationResult.Ok("Configuração carregada de " + path, "ConfigurationStore.Load");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao carregar configuração: " + ex.Message,
                "CFG_LOAD",
                "ConfigurationStore.Load",
                exception: ex);
        }
    }
}
