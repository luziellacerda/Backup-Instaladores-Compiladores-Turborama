using TurboRama.Core.Results;
using TurboRama.Windows.Exec;

namespace TurboRama.Windows.Services;

/// <summary>
/// Instala/remove serviços via sc.exe.
/// </summary>
public static class WindowsServiceInstaller
{
    public const string WatchdogServiceName = "TurboRamaWatchdog";
    public const string MaintenanceServiceName = "TurboRamaMaintenance";

    public static OperationResult CreateOrUpdate(
        string serviceName,
        string displayName,
        string binPath,
        string startType = "auto")
    {
        if (!File.Exists(binPath))
        {
            return OperationResult.Fail("Binário ausente: " + binPath, "SVC_BIN", "CreateOrUpdate");
        }

        // Para de instâncias anteriores
        ProcessRunner.Run("sc.exe", "stop \"" + serviceName + "\"", timeoutMs: 60_000, operationName: "sc-stop");
        Thread.Sleep(1000);
        ProcessRunner.Run("sc.exe", "delete \"" + serviceName + "\"", timeoutMs: 30_000, operationName: "sc-delete");
        Thread.Sleep(1500);

        // binPath= (com espaço após =). Path sem aspas aninhadas quebradas.
        // Ex.: binPath= "C:\TurboRama\App\Watchdog\TurboRama.Watchdog.exe"
        string args =
            "create \"" + serviceName + "\" " +
            "binPath= \"" + binPath + "\" " +
            "start= " + startType + " " +
            "DisplayName= \"" + displayName + "\" " +
            "obj= LocalSystem";

        OperationResult create = ProcessRunner.Run("sc.exe", args, operationName: "sc-create");
        if (!create.Success)
        {
            return create;
        }

        ProcessRunner.Run(
            "sc.exe",
            "description \"" + serviceName + "\" \"TurboRama Secure service\"",
            operationName: "sc-desc");

        // Aumenta tempo de resposta (ajuda 1053 em cold start)
        ProcessRunner.Run(
            "reg.exe",
            "add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\" /v ServicesPipeTimeout /t REG_DWORD /d 60000 /f",
            operationName: "svc-timeout");

        ProcessRunner.Run(
            "sc.exe",
            "failure \"" + serviceName + "\" reset= 86400 actions= restart/5000/restart/15000/restart/30000",
            operationName: "sc-failure");

        ProcessRunner.Run(
            "sc.exe",
            "config \"" + serviceName + "\" start= " + startType,
            operationName: "sc-config");

        return OperationResult.Ok("Serviço criado: " + serviceName + " → " + binPath, "CreateOrUpdate", currentState: binPath);
    }

    public static OperationResult Start(string serviceName)
    {
        // dá tempo ao SCM após create
        Thread.Sleep(1000);
        OperationResult r = ProcessRunner.Run(
            "sc.exe",
            "start \"" + serviceName + "\"",
            timeoutMs: 90_000,
            operationName: "sc-start");

        if (!r.Success && (r.Message.Contains("1056") || r.Message.Contains("already", StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Ok("Serviço já em execução: " + serviceName, "Start");
        }

        if (!r.Success)
        {
            // Diagnóstico: tenta rodar o binário e capturar falha imediata
            string diag = TryProbeBinary(serviceName);
            return OperationResult.Fail(
                r.Message + (string.IsNullOrEmpty(diag) ? "" : " | Diagnóstico: " + diag),
                "SVC_START",
                "Start",
                exitCode: r.ExitCode);
        }

        // confirma RUNNING
        Thread.Sleep(1500);
        OperationResult q = ProcessRunner.Run("sc.exe", "query \"" + serviceName + "\"", operationName: "sc-query");
        if (q.Success && q.Message.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok("Serviço RUNNING: " + serviceName, "Start");
        }

        return OperationResult.Ok("sc start OK (verifique services.msc): " + serviceName + " | " + q.Message, "Start");
    }

    public static OperationResult Stop(string serviceName)
    {
        OperationResult r = ProcessRunner.Run("sc.exe", "stop \"" + serviceName + "\"", timeoutMs: 60_000, operationName: "sc-stop");
        if (!r.Success && (r.Message.Contains("1062") || r.Message.Contains("not been started", StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Ok("Serviço já parado: " + serviceName, "Stop");
        }

        return r.Success
            ? OperationResult.Ok("Serviço parado: " + serviceName, "Stop")
            : r;
    }

    public static OperationResult Delete(string serviceName)
    {
        Stop(serviceName);
        Thread.Sleep(800);
        OperationResult r = ProcessRunner.Run("sc.exe", "delete \"" + serviceName + "\"", operationName: "sc-delete");
        if (!r.Success && (r.Message.Contains("1060") || r.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Ok("Serviço já inexistente: " + serviceName, "Delete");
        }

        return r.Success
            ? OperationResult.Ok("Serviço removido: " + serviceName, "Delete")
            : r;
    }

    public static bool Exists(string serviceName)
    {
        OperationResult r = ProcessRunner.Run("sc.exe", "query \"" + serviceName + "\"", operationName: "sc-query");
        return r.Success;
    }

    private static string TryProbeBinary(string serviceName)
    {
        try
        {
            string? bin = null;
            if (serviceName.Equals(WatchdogServiceName, StringComparison.OrdinalIgnoreCase))
            {
                bin = Path.Combine(Core.Paths.ProductPaths.AppWatchdog, "TurboRama.Watchdog.exe");
            }
            else if (serviceName.Equals(MaintenanceServiceName, StringComparison.OrdinalIgnoreCase))
            {
                bin = Path.Combine(Core.Paths.ProductPaths.AppMaintenance, "TurboRama.Maintenance.exe");
            }

            if (bin is null || !File.Exists(bin))
            {
                return "exe ausente";
            }

            // confere runtimeconfig
            string runtime = Path.ChangeExtension(bin, ".runtimeconfig.json");
            if (!File.Exists(runtime))
            {
                return "faltando " + Path.GetFileName(runtime);
            }

            string deps = Path.ChangeExtension(bin, ".deps.json");
            if (!File.Exists(deps))
            {
                return "faltando " + Path.GetFileName(deps);
            }

            // confere dlls de hosting
            string dir = Path.GetDirectoryName(bin)!;
            if (!File.Exists(Path.Combine(dir, "Microsoft.Extensions.Hosting.dll")) &&
                !Directory.EnumerateFiles(dir, "Microsoft.Extensions.Hosting*.dll").Any())
            {
                return "faltam DLLs Microsoft.Extensions.Hosting (publicação incompleta)";
            }

            return "binário e deps parecem presentes em " + dir;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
