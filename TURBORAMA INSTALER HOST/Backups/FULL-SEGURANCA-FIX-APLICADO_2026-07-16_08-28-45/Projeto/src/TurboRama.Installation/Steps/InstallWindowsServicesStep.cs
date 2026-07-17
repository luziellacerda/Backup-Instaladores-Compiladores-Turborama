using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.Steps;
using TurboRama.Windows.Services;

namespace TurboRama.Installation.Steps;

public sealed class InstallWindowsServicesStep : IInstallationStep
{
    public string Name => "InstallWindowsServices";
    public int Order => 90;

    public Task<OperationResult> CaptureAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        context.Properties["HadWatchdogSvc"] = WindowsServiceInstaller.Exists(WindowsServiceInstaller.WatchdogServiceName) ? "1" : "0";
        context.Properties["HadMaintSvc"] = WindowsServiceInstaller.Exists(WindowsServiceInstaller.MaintenanceServiceName) ? "1" : "0";
        return Task.FromResult(OperationResult.Ok("Estado de serviços capturado.", Name));
    }

    public Task<OperationResult> ApplyAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        string wd = Path.Combine(ProductPaths.AppWatchdog, "TurboRama.Watchdog.exe");
        string mt = Path.Combine(ProductPaths.AppMaintenance, "TurboRama.Maintenance.exe");

        OperationResult c1 = WindowsServiceInstaller.CreateOrUpdate(
            WindowsServiceInstaller.WatchdogServiceName,
            "TurboRama Watchdog",
            wd);

        if (!c1.Success)
        {
            return Task.FromResult(c1);
        }

        OperationResult c2 = WindowsServiceInstaller.CreateOrUpdate(
            WindowsServiceInstaller.MaintenanceServiceName,
            "TurboRama Maintenance",
            mt);

        if (!c2.Success)
        {
            return Task.FromResult(c2);
        }

        OperationResult s1 = WindowsServiceInstaller.Start(WindowsServiceInstaller.WatchdogServiceName);
        OperationResult s2 = WindowsServiceInstaller.Start(WindowsServiceInstaller.MaintenanceServiceName);

        string msg = "Watchdog: " + s1.Message + " | Maintenance: " + s2.Message;
        if (!s1.Success || !s2.Success)
        {
            return Task.FromResult(OperationResult.Fail(msg, "SVC_START", Name));
        }

        return Task.FromResult(OperationResult.Ok(msg, Name));
    }

    public Task<OperationResult> ValidateAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        bool wd = WindowsServiceInstaller.Exists(WindowsServiceInstaller.WatchdogServiceName);
        bool mt = WindowsServiceInstaller.Exists(WindowsServiceInstaller.MaintenanceServiceName);
        if (!wd || !mt)
        {
            return Task.FromResult(OperationResult.Fail(
                "Serviços não registrados. wd=" + wd + " mt=" + mt,
                "SVC_VAL",
                Name));
        }

        return Task.FromResult(OperationResult.Ok("Serviços Windows registrados.", Name));
    }

    public Task<OperationResult> RollbackAsync(InstallationContext context, CancellationToken cancellationToken)
    {
        var messages = new List<string>();

        if (!context.Properties.TryGetValue("HadWatchdogSvc", out string? hadWd) || hadWd != "1")
        {
            messages.Add(WindowsServiceInstaller.Delete(WindowsServiceInstaller.WatchdogServiceName).Message);
        }
        else
        {
            messages.Add("Watchdog preexistente preservado.");
        }

        if (!context.Properties.TryGetValue("HadMaintSvc", out string? hadMt) || hadMt != "1")
        {
            messages.Add(WindowsServiceInstaller.Delete(WindowsServiceInstaller.MaintenanceServiceName).Message);
        }
        else
        {
            messages.Add("Maintenance preexistente preservado.");
        }

        Core.State.MaintenanceLock.Exit();
        return Task.FromResult(OperationResult.Ok(string.Join(" | ", messages), Name));
    }
}
