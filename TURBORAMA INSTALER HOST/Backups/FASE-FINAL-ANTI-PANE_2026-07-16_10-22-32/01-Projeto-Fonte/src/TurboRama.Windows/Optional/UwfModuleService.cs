using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Windows.Exec;

namespace TurboRama.Windows.Optional;

/// <summary>
/// UWF opcional (estudo §16). Default OFF. Só edições com uwfmgr.exe.
/// </summary>
public static class UwfModuleService
{
    private static string UwfMgr => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "uwfmgr.exe");

    public static bool IsAvailable() => File.Exists(UwfMgr);

    public static OperationResult GetStatus()
    {
        if (!IsAvailable())
        {
            return OperationResult.Ok(
                "UWF: não instalado/disponível nesta edição (normal em Home/Pro). Default TurboRama=OFF.",
                "Uwf.Status",
                currentState: "NotPresent");
        }

        // Consulta curta com timeout — se travar, cai no fallback Present
        try
        {
            OperationResult get = ProcessRunner.Run(
                UwfMgr,
                "get-config",
                timeoutMs: 4000,
                operationName: "uwf-get-config");
            string body = (get.Message ?? string.Empty);
            if (body.Length > 280)
            {
                body = body[..280] + "…";
            }

            bool filterOn =
                body.Contains("Filter state", StringComparison.OrdinalIgnoreCase) &&
                (body.Contains("ON", StringComparison.OrdinalIgnoreCase) ||
                 body.Contains("Enabled", StringComparison.OrdinalIgnoreCase));

            return OperationResult.Ok(
                "UWF: instalado. " +
                (get.Success
                    ? ("consulta=" + (string.IsNullOrWhiteSpace(body) ? "ok" : body.Replace('\n', ' ')))
                    : "consulta limitada/timeout (seguro).") +
                " | Default produto=OFF se não habilitado na Fase 4.",
                "Uwf.Status",
                currentState: filterOn ? "LikelyOn" : "Present");
        }
        catch (Exception ex)
        {
            return OperationResult.Ok(
                "UWF: uwfmgr presente; detalhe indisponível (" + ex.Message + ").",
                "Uwf.Status",
                currentState: "Present");
        }
    }

    public static OperationResult EnableWithExclusions()
    {
        if (!IsAvailable())
        {
            return OperationResult.Fail(
                "UWF não disponível. Use Windows IoT/Enterprise com Device Lockdown.",
                "UWF_NA",
                "Uwf.Enable");
        }

        // Exclusões obrigatórias (estudo)
        string[] exclusions =
        {
            ProductPaths.Data,
            ProductPaths.Saves,
            ProductPaths.Logs,
            ProductPaths.Config,
            ProductPaths.State,
            ProductPaths.Backup,
            ProductPaths.Updates,
            ProductPaths.App,
            ProductPaths.Frontend,
        };

        var steps = new List<string>();
        foreach (string path in exclusions)
        {
            Directory.CreateDirectory(path);
            OperationResult ex = ProcessRunner.Run(
                UwfMgr,
                "file add-exclusion \"" + path + "\"",
                operationName: "uwf-excl");
            steps.Add(path + "=" + (ex.Success ? "OK" : "AVISO"));
        }

        ProcessRunner.Run(UwfMgr, "overlay set-size 2048", operationName: "uwf-overlay");
        ProcessRunner.Run(UwfMgr, "volume protect C:", operationName: "uwf-vol");
        OperationResult enable = ProcessRunner.Run(UwfMgr, "filter enable", operationName: "uwf-enable");
        if (!enable.Success)
        {
            return OperationResult.Fail(
                "Falha ao habilitar UWF: " + enable.Message + " | " + string.Join("; ", steps),
                "UWF_ENABLE",
                "Uwf.Enable");
        }

        return OperationResult.Ok(
            "UWF habilitado (próximo boot). Exclusões: " + string.Join(", ", exclusions.Select(Path.GetFileName)),
            "Uwf.Enable",
            currentState: "EnabledPendingReboot");
    }

    public static OperationResult Disable()
    {
        if (!IsAvailable())
        {
            return OperationResult.Ok("UWF não aplicável.", "Uwf.Disable");
        }

        OperationResult r = ProcessRunner.Run(UwfMgr, "filter disable", operationName: "uwf-disable");
        return r.Success
            ? OperationResult.Ok("UWF desabilitado (próximo boot).", "Uwf.Disable")
            : OperationResult.Fail(r.Message, "UWF_DISABLE", "Uwf.Disable");
    }
}
