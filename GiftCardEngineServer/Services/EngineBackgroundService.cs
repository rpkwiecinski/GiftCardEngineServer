using Microsoft.Extensions.Hosting;

namespace GiftCardEngine.Services;

public class EngineBackgroundService : BackgroundService
{
    private readonly IEngineScheduler _scheduler;

    public EngineBackgroundService(IEngineScheduler scheduler) => _scheduler = scheduler;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _scheduler.ProcessQueueAsync(stoppingToken);
    }
}
