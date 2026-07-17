using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Watchdog;

// Windows Service + console debug.
// SEMPRE registra Windows Service lifetime — o Host só ativa SCM quando rodando como serviço.

Directory.CreateDirectory(ProductPaths.Logs);
try { ProductPaths.EnsureLayout(); } catch { /* ignore */ }

string logDir = Directory.Exists(ProductPaths.WatchdogLogs)
    ? ProductPaths.WatchdogLogs
    : Path.GetTempPath();

var logger = new FileTurboRamaLogger(logDir, "watchdog");
logger.Info("Watchdog", "Process start PID=" + Environment.ProcessId +
    " interactive=" + Environment.UserInteractive +
    " base=" + AppContext.BaseDirectory);

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    // Crítico para evitar 1053: lifetime de serviço Windows
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "TurboRamaWatchdog";
    });

    builder.Services.AddSingleton<ITurboRamaLogger>(logger);
    builder.Services.AddHostedService<WatchdogHostedService>();

    // Evita hang em content root inválido
    builder.Environment.ContentRootPath = AppContext.BaseDirectory;

    IHost host = builder.Build();
    logger.Info("Watchdog", "Host built — RunAsync");
    await host.RunAsync().ConfigureAwait(false);
    logger.Info("Watchdog", "Host stopped cleanly");
}
catch (Exception ex)
{
    logger.Error("Watchdog", "FATAL: " + ex, errorCode: "WD_FATAL");
    try
    {
        File.WriteAllText(
            Path.Combine(logDir, "watchdog-fatal.txt"),
            DateTimeOffset.Now + Environment.NewLine + ex);
    }
    catch { /* ignore */ }

    // Em console, rethrow; em serviço o SCM precisa de exit code
    Environment.ExitCode = 1;
    throw;
}
