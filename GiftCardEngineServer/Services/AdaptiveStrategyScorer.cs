using GiftCardEngine.Models;
using System.Collections.Concurrent;

namespace GiftCardEngine.Services;


public class AdaptiveStrategyScorer : IAdaptiveStrategyScorer
{
    private ConcurrentDictionary<string, int> _scores = new();
    private ConcurrentQueue<string> _recentWinners = new();

    public void UpdateWithResult(EngineRunResult result)
    {
        foreach (var kv in result.StrategiesScoring)
            _scores.AddOrUpdate(kv.Key, kv.Value, (_, old) => old + kv.Value);

        if (result.StrategiesScoring.OrderByDescending(x => x.Value).FirstOrDefault().Key is string winner)
        {
            _recentWinners.Enqueue(winner);
            if (_recentWinners.Count > 100) _recentWinners.TryDequeue(out _);
        }
    }

    public object GetBestStrategies()
    {
        return _scores.OrderByDescending(x => x.Value).Take(5).ToList();
    }
}
