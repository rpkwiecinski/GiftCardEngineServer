using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GiftCardEngine.Models;
using GiftCardEngine.Services;
using GiftCardBaskets.Engines;
using GiftCardBaskets.Core;
using System.Collections.Generic;

public class EngineBackgroundService : BackgroundService
{
    private readonly ILogger<EngineBackgroundService> _logger;
    private readonly ProfitPlannerHybrid _engine;
    private readonly IResultRepository _results;
    private readonly List<Game> _catalogue;

    public EngineBackgroundService(
        ILogger<EngineBackgroundService> logger,
        ProfitPlannerHybrid engine,
        IResultRepository results,
        List<Game> catalogue)
    {
        _logger = logger;
        _engine = engine;
        _results = results;
        _catalogue = catalogue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EngineBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Tutaj uruchamiasz silnik i zbierasz wynik
                var baskets = await _engine.BuildAsync(_catalogue, workers: 2, dailyLimit: 50, stoppingToken);
                var result = new EngineRunResult
                {
                    Timestamp = DateTime.UtcNow,
                    Job = new EngineJobRequest { JobName = "continuous" }, // dostosuj jak masz inne wymagania
                    Baskets = baskets
                };

                _results.SaveResult(result);
                _logger.LogInformation("Background learning: new result saved at {Time}", result.Timestamp);

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // odświeżanie co 30s
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during engine background learning");
            }
        }
    }
}
