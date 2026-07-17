using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Maintenance;

Directory.CreateDirectory(ProductPaths.Logs);
try { ProductPaths.EnsureLayout(); } catch { /* ignore */ }

string logDir = Directory.Exists(ProductPaths.MaintenanceLogs)
    ? ProductPaths.MaintenanceLogs
    : Path.GetTempPath();

var logger = new FileTurboRamaLogger(logDir, "maintenance");
logger.Info("Maintenance", "Process start PID=" + Environment.ProcessId +
    " interactive=" + Environment.UserInteractive +
    " base=" + AppContext.BaseDirectory);

if (Environment.UserInteractive)
{
    Console.WriteLine("TurboRama.Maintenance — pipe TurboRamaMaintenance (também funciona como serviço).");
}

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "TurboRamaMaintenance";
    });

    builder.Services.AddSingleton<ITurboRamaLogger>(logger);
    builder.Services.AddHostedService<MaintenanceHostedService>();
    builder.Environment.ContentRootPath = AppContext.BaseDirectory;

    IHost host = builder.Build();
    logger.Info("Maintenance", "Host built — RunAsync");
    await host.RunAsync().ConfigureAwait(false);
    logger.Info("Maintenance", "Host stopped cleanly");
}
catch (Exception ex)
{
    logger.Error("Maintenance", "FATAL: " + ex, errorCode: "MT_FATAL");
    try
    {
        File.WriteAllText(
            Path.Combine(logDir, "maintenance-fatal.txt"),
            DateTimeOffset.Now + Environment.NewLine + ex);
    }
    catch { /* ignore */ }

    Environment.ExitCode = 1;
    throw;
}
