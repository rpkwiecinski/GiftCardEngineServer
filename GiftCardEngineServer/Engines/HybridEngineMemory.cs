using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GiftCardBaskets.Engines
{
    public class StrategyStats
    {
        public string Strategy { get; set; }
        public decimal BestProfit { get; set; }
        public decimal AvgProfit { get; set; }
        public int Runs { get; set; }
        public int SuccessRuns { get; set; }
    }

    public class HybridEngineMemory
    {
        private readonly Dictionary<string, StrategyStats> _stats = new();

        public void Update(string strategy, decimal profit, bool success)
        {
            if (!_stats.TryGetValue(strategy, out var stat))
            {
                stat = new StrategyStats { Strategy = strategy, Runs = 0, SuccessRuns = 0 };
                _stats[strategy] = stat;
            }
            stat.Runs++;
            stat.AvgProfit = (stat.AvgProfit * (stat.Runs - 1) + profit) / stat.Runs;
            if (profit > stat.BestProfit) stat.BestProfit = profit;
            if (success) stat.SuccessRuns++;
        }

        public void Save(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(_stats.Values, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void Load(string path)
        {
            if (File.Exists(path))
            {
                var stats = JsonSerializer.Deserialize<List<StrategyStats>>(File.ReadAllText(path));
                foreach (var s in stats)
                    _stats[s.Strategy] = s;
            }
        }

        public Dictionary<string, StrategyStats> GetAllStats() => _stats;
    }
}
