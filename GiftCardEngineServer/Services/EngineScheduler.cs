using GiftCardEngine.Models;
using GiftCardBaskets.Engines;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GiftCardEngine.Services
{
    public class EngineScheduler : IEngineScheduler
    {
        private readonly IResultRepository _repo;
        private readonly IAdaptiveStrategyScorer _scorer;
        private readonly ConcurrentQueue<EngineJobRequest> _queue = new();
        private readonly object _lock = new();
        private bool _isRunning = false;
        private int _jobsDone = 0;
        private EngineRunResult? _lastResult;
        private DateTime? _lastRun;
        private Dictionary<string, StrategyStats> _strategyStats = new();

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
                    lock (_lock) _isRunning = true;
                    var result = await RunEngine(job, ct);
                    _repo.SaveResult(result);
                    _jobsDone++;
                    _scorer.UpdateWithResult(result);
                    lock (_lock)
                    {
                        _isRunning = false;
                        _lastResult = result;
                        _lastRun = DateTime.UtcNow;
                        if (result.StrategiesStats != null)
                            _strategyStats = result.StrategiesStats;
                    }
                }
                else
                {
                    await Task.Delay(1000, ct);
                }
            }
        }

        public EngineStatus GetStatus()
        {
            var bestStrategies = _scorer.GetBestStrategies();
            var allStats = _repo.GetLastResults()
                                .Where(r => r.StrategiesStats != null)
                                .SelectMany(r => r.StrategiesStats.Values)
                                .ToList();

            var globalStats = allStats
                .GroupBy(s => s.Strategy)
                .ToDictionary(
                    g => g.Key,
                    g => new StrategyStats
                    {
                        Strategy = g.Key,
                        Runs = g.Sum(x => x.Runs),
                        SuccessRuns = g.Sum(x => x.SuccessRuns),
                        AvgProfit = g.Any() ? g.Average(x => x.AvgProfit) : 0,
                        BestProfit = g.Any() ? g.Max(x => x.BestProfit) : 0,
                        AvgWaste = g.Any() ? g.Average(x => x.AvgWaste) : 0,
                        MinWaste = g.Any() ? g.Min(x => x.MinWaste) : decimal.MaxValue,
                        MaxWaste = g.Any() ? g.Max(x => x.MaxWaste) : decimal.MinValue
                    });

            return new EngineStatus
            {
                Running = _isRunning,
                Queue = _queue.Count,
                JobsDone = _jobsDone,
                BestStrategies = bestStrategies,
                StrategiesStats = globalStats,
                LastRun = _lastRun,
                LastJobResult = _lastResult
            };
        }

        private async Task<EngineRunResult> RunEngine(EngineJobRequest job, CancellationToken ct)
        {
            var catalogue = GameLoader.Load(job.CataloguePath);
            var engine = new ProfitPlannerHybrid();
            var baskets = await engine.BuildAsync(catalogue, job.Workers, job.DailyLimit, ct);

            // jeśli nie masz Memory to daj pusty słownik!
            var strategiesStats = new Dictionary<string, StrategyStats>();

            return new EngineRunResult
            {
                Job = job,
                Profit = baskets.Sum(b => b.NetProfit),
                Waste = baskets.Sum(b => b.Waste),
                BasketCount = baskets.Count,
                Timestamp = DateTime.UtcNow,
                StrategiesScoring = engine.GetStrategyScores(),
                Baskets = baskets,
                StrategiesStats = strategiesStats,
                Catalogue = catalogue,
                PlannerResult = engine.Plan,
                EngineTag = engine.Name
            };
        }
    }
}
