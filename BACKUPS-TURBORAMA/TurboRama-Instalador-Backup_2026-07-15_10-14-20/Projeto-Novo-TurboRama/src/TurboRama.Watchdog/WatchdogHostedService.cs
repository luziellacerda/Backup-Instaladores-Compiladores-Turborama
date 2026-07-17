using Microsoft.Extensions.Hosting;
using TurboRama.Core.Logging;

namespace TurboRama.Watchdog;

public sealed class WatchdogHostedService : BackgroundService
{
    private readonly ITurboRamaLogger _logger;

    public WatchdogHostedService(ITurboRamaLogger logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var worker = new WatchdogWorker(_logger);
        return worker.RunAsync(stoppingToken);
    }
}
