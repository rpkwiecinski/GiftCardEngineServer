using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GiftCardBaskets.Core;
using GiftCardEngine.Services;

namespace GiftCardBaskets.Engines
{
    public sealed class ProfitPlannerHybrid
    {
        private IAdaptiveStrategyScorer _scorer;
        private Dictionary<string, int> _localStrategyScores = new();

        public ProfitPlannerHybrid(IAdaptiveStrategyScorer scorer)
        {
            _scorer = scorer;
        }
        private static void ShuffleList<T>(List<T> list, Random rand)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        public async Task<List<Basket>> BuildAsync(
            List<Game> catalogue,
            int workers,
            int dailyLimit,
            CancellationToken ct = default)
        {
            int seconds = 60;
            DateTime stopAt = DateTime.UtcNow.AddSeconds(seconds);
            var strategies = new Dictionary<string, Func<List<Game>, CancellationToken, List<Basket>>>
            {
                //{ "Profit", BuildProfitBaskets },
                //{ "Rating", BuildRatingBaskets },
                //{ "Demand", BuildDemandBaskets },
                //{ "RandomGreedy", BuildRandomGreedyBaskets },
                //{ "HighMargin", BuildHighMarginFirstBaskets },
                //{ "MaxFill", BuildMaximizeBasketFillBaskets },
                //{ "Cheapest", BuildCheapestGamesFirstBaskets },
                //{ "MixHighLow", BuildMixHighAndLowRatingBaskets },
            };
            var stratNames = strategies.Keys.ToArray();
            var rand = new Random();
            var rollingScores = stratNames.ToDictionary(n => n, _ => 0);
            var localLog = new List<(DateTime ts, int stratIdx, decimal profit, decimal waste, int count)>();

            decimal bestProfit = decimal.MinValue;
            List<Basket> best = null;
            int[] stratUsage = new int[stratNames.Length];

            while (DateTime.UtcNow < stopAt && !ct.IsCancellationRequested)
            {
                int stratIdx = WeightedRandomIndex(rollingScores.Values.ToArray(), rand);
                var strat = strategies.ElementAt(stratIdx);
                var g = catalogue.Select(x => x.Clone()).ToList();
                ShuffleList(g, rand);
                var baskets = strat.Value(g, ct);

                decimal profit = baskets.Sum(x => x.NetProfit);
                decimal waste = baskets.Sum(x => x.Waste);
                int cnt = baskets.Count;

                stratUsage[stratIdx]++;
                localLog.Add((DateTime.UtcNow, stratIdx, profit, waste, cnt));

                // Rolling scoring logic
                UpdateRollingScores(rollingScores, stratNames, stratIdx, profit, localLog);

                if (profit > bestProfit)
                {
                    bestProfit = profit;
                    best = baskets.Select(x => x.Clone()).ToList();
                }

                // Co X iteracji resetuj rolling window
                if (localLog.Count % 20 == 0)
                {
                    foreach (var key in rollingScores.Keys.ToList())
                        rollingScores[key] = 0;
                }
            }

            _localStrategyScores = rollingScores;
            LogAdaptiveSessionResults(stratNames, stratUsage, localLog, bestProfit);

            return best ?? new List<Basket>();
        }

        public Dictionary<string, int> GetStrategyScores() => _localStrategyScores;

        private static int WeightedRandomIndex(int[] weights, Random rand)
        {
            int total = weights.Sum();
            if (total == 0) return rand.Next(weights.Length);
            int r = rand.Next(total);
            int cum = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                cum += weights[i];
                if (r < cum) return i;
            }
            return rand.Next(weights.Length);
        }

        private void UpdateRollingScores(
       Dictionary<string, int> scores, string[] names, int idx, decimal profit,
       List<(DateTime, int, decimal, decimal, int)> log)
        {
            // Ostatnie 10
            var window = log.Skip(Math.Max(0, log.Count - 10)).ToList();
            var order = window.OrderByDescending(e => e.Item3).ToList(); // e.Item3 = profit
            if (order.Count > 0) scores[names[order[0].Item2]] += 2;    // Item2 = stratIdx
            if (order.Count > 1) scores[names[order[1].Item2]] += 1;
        }


        private void LogAdaptiveSessionResults(string[] stratNames, int[] usage, List<(DateTime ts, int stratIdx, decimal profit, decimal waste, int basketCount)> log, decimal bestProfit)
        {
            var obj = new
            {
                stratNames,
                usage,
                log,
                bestProfit
            };
            var json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText($"history/session_{DateTime.UtcNow:yyyyMMddHHmmssfff}.json", json);
        }

        // ...strategies here...
        // Implement all required static strategies from previous versions
        // plus shuffle, mutate, etc.
    }
}
