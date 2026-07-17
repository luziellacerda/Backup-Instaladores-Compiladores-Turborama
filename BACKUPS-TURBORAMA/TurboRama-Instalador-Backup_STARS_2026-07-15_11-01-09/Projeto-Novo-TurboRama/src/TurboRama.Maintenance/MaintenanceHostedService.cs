using Microsoft.Extensions.Hosting;
using TurboRama.Core.Logging;

namespace TurboRama.Maintenance;

public sealed class MaintenanceHostedService : BackgroundService
{
    private readonly ITurboRamaLogger _logger;

    public MaintenanceHostedService(ITurboRamaLogger logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var server = new MaintenancePipeServer(_logger);
        return server.RunAsync(stoppingToken);
    }
}
