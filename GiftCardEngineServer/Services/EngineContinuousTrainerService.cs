using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GiftCardBaskets.Core;
using GiftCardBaskets.Engines;
using System.Collections.Generic;
using GiftCardEngine.Models;

public sealed class EngineContinuousTrainerService : BackgroundService
{
    private readonly ProfitPlannerHybrid _engine;
    private readonly EngineTrainerResultsHolder _results;
    private readonly ILogger<EngineContinuousTrainerService> _logger;
    private readonly int _workers;
    private readonly int _dailyLimit;
    private readonly List<Game> _catalogue;

    public EngineContinuousTrainerService(
        ProfitPlannerHybrid engine,
        EngineTrainerResultsHolder results,
        ILogger<EngineContinuousTrainerService> logger,
        int workers,
        int dailyLimit,
        List<Game> catalogue)
    {
        _engine = engine;
        _results = results;
        _logger = logger;
        _workers = workers;
        _dailyLimit = dailyLimit;
        _catalogue = catalogue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EngineContinuousTrainerService started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var baskets = await _engine.BuildAsync(
                    _catalogue.Select(x => x.Clone()).ToList(),
                    _workers,
                    _dailyLimit,
                    stoppingToken);

                var result = new EngineRunResult
                {
                    EngineTag = _engine.Name,
                    Catalogue = _catalogue.Select(x => x.Clone()).ToList(),
                    Baskets = baskets,
                    PlannerResult = _engine.Plan
                };

                _results.SetResult(result);
                _logger.LogInformation("Continuous learning: New engine result updated [{Time}]", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during engine background learning");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); // adjust interval as needed
        }
    }
}
