using GiftCardEngine.Models;
using GiftCardBaskets.Engines;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;

namespace GiftCardEngine.Services;

public class EngineScheduler : IEngineScheduler
{
    private readonly IResultRepository _repo;
    private readonly IAdaptiveStrategyScorer _scorer;
    private ConcurrentQueue<EngineJobRequest> _queue = new();
    private bool _isRunning = false;
    private int _jobsDone = 0;

    public EngineScheduler(IResultRepository repo, IAdaptiveStrategyScorer scorer)
    {
        _repo = repo;
        _scorer = scorer;
    }

    public async Task<EngineJobResult> RunJobAsync(EngineJobRequest req)
    {
        _queue.Enqueue(req);
        return await Task.FromResult(new EngineJobResult { Accepted = true });
    }

    public async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_queue.TryDequeue(out var job))
            {
                _isRunning = true;
                var result = await RunEngine(job, ct);
                _repo.SaveResult(result);
                _jobsDone++;
                _scorer.UpdateWithResult(result);
                _isRunning = false;
            }
            else
            {
                await Task.Delay(1000, ct);
            }
        }
    }

    public object GetStatus()
    {
        return new
        {
            running = _isRunning,
            queue = _queue.Count,
            jobsDone = _jobsDone,
            bestStrategies = _scorer.GetBestStrategies()
        };
    }

    private async Task<EngineRunResult> RunEngine(EngineJobRequest job, CancellationToken ct)
    {
        // Wczytaj katalog
        var catalogue = GameLoader.Load(job.CataloguePath);
        var engine = new ProfitPlannerHybrid(); 
        var baskets = await engine.BuildAsync(catalogue, job.Workers, job.DailyLimit, ct);
        return new EngineRunResult
        {
            Job = job,
            Profit = baskets.Sum(b => b.NetProfit),
            Waste = baskets.Sum(b => b.Waste),
            BasketCount = baskets.Count,
            Timestamp = DateTime.UtcNow,
            StrategiesScoring = engine.GetStrategyScores()
        };
    }

}
