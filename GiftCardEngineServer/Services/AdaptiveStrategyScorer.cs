using GiftCardEngine.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace GiftCardEngine.Services
{
    public class AdaptiveStrategyScorer : IAdaptiveStrategyScorer
    {
        private readonly ConcurrentDictionary<string, int> _scores = new();
        private readonly ConcurrentQueue<string> _recentWinners = new();

        public void UpdateWithResult(EngineRunResult result)
        {
            // Zakładamy że StrategiesScoring to Dictionary<string, int>
            if (result.StrategiesScoring == null)
                return;

            foreach (var kv in result.StrategiesScoring)
                _scores.AddOrUpdate(kv.Key, kv.Value, (_, old) => old + kv.Value);

            var winner = result.StrategiesScoring
                .OrderByDescending(x => x.Value)
                .FirstOrDefault().Key;

            if (!string.IsNullOrEmpty(winner))
            {
                _recentWinners.Enqueue(winner);
                if (_recentWinners.Count > 100)
                    _recentWinners.TryDequeue(out _);
            }
        }

        public List<KeyValuePair<string, int>> GetBestStrategies()
        {
            return _scores.OrderByDescending(x => x.Value).Take(5).ToList();
        }
    }
}
