using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GiftCardBaskets.Core;

namespace GiftCardBaskets.Engines
{
    public sealed class ProfitPlannerHybrid : IEngine, IScheduleProvider
    {
        public string Name => "PROFIT-HYBRID-PLANNER";
        public PlannerResult Plan { get; private set; }
            = new PlannerResult(Array.Empty<DayPlan>(), Config.DefaultDailyCap, Config.DefaultWorkers);

        private Dictionary<string, int> _localStrategyScores = new();

        public ProfitPlannerHybrid()
        {
        }

        public Dictionary<string, int> GetStrategyScores() => _localStrategyScores;

        public async Task<List<Basket>> BuildAsync(
            List<Game> catalogue,
            int workers,
            int dailyLimit,
            CancellationToken ct = default)
        {
            int seconds = 21600;
            int metaSwitchSeconds = 2160;
            int metaResetSeconds = 700;

            var strategies = new Func<List<Game>, CancellationToken, List<Basket>>[]
            {
                BuildProfitBaskets,
                BuildRatingBaskets,
                BuildDemandBaskets,
                BuildRandomGreedyBaskets,
                BuildHighMarginFirstBaskets,
                BuildMaximizeBasketFillBaskets,
                BuildCheapestGamesFirstBaskets,
                BuildMixHighAndLowRatingBaskets,
                BuildHighestAbsoluteProfitFirstBaskets,
                BuildMostBalancedBasketMixBaskets,
                BuildFamilyMinimizingWasteBaskets,
            };

            string[] stratNames = new string[]
            {
                "Profit",
                "Rating",
                "Demand",
                "RandomGreedy",
                "HighMarginFirst",
                "MaxBasketFill",
                "CheapestFirst",
                "MixHiLo",
                "HighestAbsProfit",
                "BalancedMix",
                "MinWaste"
            };

            int stratCount = strategies.Length;
            var rand = new Random();

            List<Basket> best = null;
            decimal bestProfit = decimal.MinValue;

            var stratScore = new int[stratCount];
            var stratUsage = new int[stratCount];
            var stratResults = new List<(int stratIdx, decimal profit, List<Basket> baskets)>();
            var runLog = new List<(DateTime ts, int stratIdx, decimal profit, decimal waste, int basketCount)>();

            bool adaptiveMode = false;
            int[] adaptiveOrder = null;
            int[] adaptiveWeights = null;

            DateTime stopAt = DateTime.UtcNow.AddSeconds(seconds);
            DateTime metaSwitchAt = DateTime.UtcNow.AddSeconds(metaSwitchSeconds);
            DateTime lastScoreReset = DateTime.UtcNow;

            while (DateTime.UtcNow < stopAt && !ct.IsCancellationRequested)
            {
                int stratIdx;
                if (adaptiveMode && adaptiveOrder != null && adaptiveWeights != null)
                {
                    int totalWeight = adaptiveWeights.Sum();
                    int r = rand.Next(totalWeight);
                    int accum = 0, pick = 0;
                    for (pick = 0; pick < adaptiveOrder.Length; pick++)
                    {
                        accum += adaptiveWeights[pick];
                        if (r < accum) break;
                    }
                    stratIdx = adaptiveOrder[pick];
                }
                else
                {
                    stratIdx = rand.Next(stratCount);
                }

                stratUsage[stratIdx]++;
                var strat = strategies[stratIdx];
                var g = catalogue.Select(x => x.Clone()).ToList();
                ShuffleList(g, rand);
                var baskets = strat(g, ct);
                MutateBasketsLocal(g, baskets, rand);

                decimal profit = baskets.Sum(x => x.NetProfit);
                decimal waste = baskets.Sum(x => x.WasteValueEur);

                runLog.Add((DateTime.UtcNow, stratIdx, profit, waste, baskets.Count));
                stratResults.Add((stratIdx, profit, baskets));

                if (profit > bestProfit)
                {
                    bestProfit = profit;
                    best = baskets.Select(x => x.Clone()).ToList();
                }

                // --- Evolutionary scoring (pseudo-learning): after every round, top3 get points ---
                var lastRound = stratResults.TakeLast(stratCount)
                    .OrderByDescending(x => x.profit)
                    .Select((val, idx) => (val.stratIdx, idx)).ToList();

                foreach (var (sidx, pos) in lastRound)
                {
                    if (pos == 0) stratScore[sidx] += 2;
                    else if (pos == 1) stratScore[sidx] += 1;
                }

                // --- Switch to adaptive mode after metaSwitchSeconds ---
                if (!adaptiveMode && DateTime.UtcNow >= metaSwitchAt)
                {
                    var topStrats = stratScore
                        .Select((score, idx) => (score, idx))
                        .OrderByDescending(x => x.score)
                        .Take(3)
                        .ToList();

                    adaptiveOrder = topStrats.Select(x => x.idx).ToArray();
                    adaptiveWeights = topStrats.Select(x => Math.Max(1, x.score)).ToArray();
                    adaptiveMode = true;
                }

                // --- Reset scory co metaResetSeconds ---
                if ((DateTime.UtcNow - lastScoreReset).TotalSeconds >= metaResetSeconds)
                {
                    Array.Clear(stratScore, 0, stratCount);
                    runLog.Clear();
                    stratResults.Clear();
                    lastScoreReset = DateTime.UtcNow;
                    if (adaptiveMode)
                    {
                        adaptiveMode = false;
                        adaptiveOrder = null;
                        adaptiveWeights = null;
                        metaSwitchAt = DateTime.UtcNow.AddSeconds(metaSwitchSeconds);
                    }
                }
            }

            Plan = BuildCalendar(best ?? new List<Basket>(), workers, dailyLimit);
            LogAdaptiveSessionResults(stratNames, stratUsage, runLog, bestProfit);

            _localStrategyScores = new Dictionary<string, int>();
            for (int i = 0; i < stratNames.Length; i++)
                _localStrategyScores[stratNames[i]] = stratScore[i];

            return best ?? new List<Basket>();
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
            Directory.CreateDirectory("history");
            File.WriteAllText($"history/session_{DateTime.UtcNow:yyyyMMddHHmmssfff}.json", json);
        }

        // ======================== STRATEGIE ============================

        private static List<Basket> BuildProfitBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var order = Enumerable.Range(0, g.Count)
                                  .OrderByDescending(i => g[i].Price * g[i].ProfitPct * g[i].Rating * g[i].Remaining)
                                  .ToArray();

            while (!ct.IsCancellationRequested)
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildRatingBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var order = Enumerable.Range(0, g.Count)
                                  .OrderByDescending(i => g[i].Rating * g[i].ProfitPct * g[i].Price * g[i].Remaining)
                                  .ToArray();

            while (!ct.IsCancellationRequested)
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildDemandBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var order = Enumerable.Range(0, g.Count)
                                  .OrderByDescending(i => g[i].Required > 0
                                            ? (double)g[i].Remaining / g[i].Required
                                            : 0.0)
                                  .ToArray();

            while (!ct.IsCancellationRequested && g.Any(x => x.Remaining > 0))
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.Games.Count > best.Games.Count)
                        best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games)
                    if (gm.Remaining > 0)
                        gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildRandomGreedyBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var rand = new Random();
            while (!ct.IsCancellationRequested && g.Any(x => x.Remaining > 0))
            {
                var available = g.Select((val, idx) => (val, idx)).Where(x => x.val.Remaining > 0).ToArray();
                if (available.Length == 0) break;
                int focus = available[rand.Next(available.Length)].idx;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildHighMarginFirstBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var order = Enumerable.Range(0, g.Count)
                .OrderByDescending(i => g[i].ProfitPct)
                .ThenByDescending(i => g[i].Rating)
                .ToArray();

            while (!ct.IsCancellationRequested)
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildMaximizeBasketFillBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var order = Enumerable.Range(0, g.Count)
                .OrderByDescending(i => g[i].Price)
                .ToArray();

            while (!ct.IsCancellationRequested)
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = GreedyFill(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildCheapestGamesFirstBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var order = Enumerable.Range(0, g.Count)
                .OrderBy(i => g[i].Price)
                .ThenByDescending(i => g[i].ProfitPct)
                .ToArray();

            while (!ct.IsCancellationRequested)
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildMixHighAndLowRatingBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var orderHigh = Enumerable.Range(0, g.Count)
                .OrderByDescending(i => g[i].Rating)
                .ToArray();
            var orderLow = Enumerable.Range(0, g.Count)
                .OrderBy(i => g[i].Rating)
                .ToArray();

            int toggle = 0;
            while (!ct.IsCancellationRequested)
            {
                var order = toggle++ % 2 == 0 ? orderHigh : orderLow;
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildHighestAbsoluteProfitFirstBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var order = Enumerable.Range(0, g.Count)
                .OrderByDescending(i => g[i].Price * g[i].ProfitPct)
                .ToArray();

            while (!ct.IsCancellationRequested)
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildMostBalancedBasketMixBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            var usageCount = new Dictionary<string, int>();
            foreach (var game in g) usageCount[game.Title] = 0;

            while (!ct.IsCancellationRequested)
            {
                var order = Enumerable.Range(0, g.Count)
                    .OrderBy(i => usageCount[g[i].Title])
                    .ThenByDescending(i => g[i].ProfitPct * g[i].Rating)
                    .ToArray();

                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (best == null || b.NetProfit > best.NetProfit) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games)
                {
                    if (gm.Remaining > 0) gm.Remaining--;
                    usageCount[gm.Title]++;
                }
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        private static List<Basket> BuildFamilyMinimizingWasteBaskets(List<Game> g, CancellationToken ct)
        {
            var baskets = new List<Basket>();
            decimal wasteThreshold = 1.0m;
            var order = Enumerable.Range(0, g.Count)
                .OrderByDescending(i => g[i].ProfitPct * g[i].Rating)
                .ToArray();

            while (!ct.IsCancellationRequested)
            {
                int focus = order.FirstOrDefault(i => g[i].Remaining > 0);
                if (focus < 0) break;
                Basket? best = null;
                foreach (int card in Config.Cards.OrderByDescending(c => c))
                {
                    if (g[focus].Price > card) continue;
                    var picks = Greedy(card, focus, g);
                    if (picks == null) continue;
                    var b = new Basket(card);
                    foreach (int ix in picks) b.Games.Add(g[ix]);
                    if (b.Waste <= wasteThreshold && (best == null || b.NetProfit > best.NetProfit)) best = b;
                }
                if (best == null) break;
                foreach (var gm in best.Games) if (gm.Remaining > 0) gm.Remaining--;
                baskets.Add(best);
            }
            Retry.Do(g, baskets);
            return baskets;
        }

        // ======================== GREEDY, FILL, MUTATE, SHUFFLE ============================

        private static List<int>? Greedy(int card, int focus, List<Game> g)
        {
            var picks = new List<int> { focus };
            decimal sum = g[focus].Price;
            var cand = Enumerable.Range(0, g.Count)
                .Where(i => i != focus && g[i].Remaining > 0)
                .OrderByDescending(i => g[i].Price * g[i].ProfitPct * g[i].Rating)
                .ToArray();

            foreach (int idx in cand)
            {
                if (picks.Count >= Config.MaxItems) break;
                var gm = g[idx];
                if (Rules.ViolatesFamilyExclusive(gm, picks.Select(p => g[p]))) continue;
                if (sum + gm.Price > card) continue;

                picks.Add(idx);
                sum += gm.Price;
                if (BasketRules.MeetsRequirements(card, picks, sum)) break;
            }
            return BasketRules.MeetsRequirements(card, picks, sum) ? picks : null;
        }

        private static List<int>? GreedyFill(int card, int focus, List<Game> g)
        {
            var picks = new List<int> { focus };
            decimal sum = g[focus].Price;
            var cand = Enumerable.Range(0, g.Count)
                .Where(i => i != focus && g[i].Remaining > 0)
                .OrderByDescending(i => g[i].Price)
                .ToArray();

            foreach (int idx in cand)
            {
                if (picks.Count >= Config.MaxItems) break;
                var gm = g[idx];
                if (Rules.ViolatesFamilyExclusive(gm, picks.Select(p => g[p]))) continue;
                if (sum + gm.Price > card) continue;
                picks.Add(idx);
                sum += gm.Price;
            }
            return BasketRules.MeetsRequirements(card, picks, sum) ? picks : null;
        }

        private static void MutateBasketsLocal(List<Game> catalogue, List<Basket> baskets, Random rand)
        {
            if (baskets == null || baskets.Count == 0) return;
            foreach (var basket in baskets)
            {
                for (int i = 0; i < basket.Games.Count; i++)
                {
                    var oldGame = basket.Games[i];
                    var better = catalogue
                        .Where(g => g.Remaining > 0 &&
                                    g.Price <= (basket.Card - (basket.Games.Sum(x => x.Price) - oldGame.Price)) &&
                                    g.ProfitPct * g.Rating > oldGame.ProfitPct * oldGame.Rating &&
                                    !Rules.ViolatesFamilyExclusive(g, basket.Games.Where((_, j) => j != i)))
                        .OrderByDescending(g => g.ProfitPct * g.Rating)
                        .FirstOrDefault();
                    if (better != null && better.Title != oldGame.Title)
                    {
                        basket.Games[i] = better;
                        better.Remaining--;
                        oldGame.Remaining++;
                        break;
                    }
                }
            }
        }

        private static void ShuffleList<T>(List<T> list, Random rand)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                var temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        private static PlannerResult BuildCalendar(List<Basket> all, int workers, int dailyLimit)
        {
            if (all.Count == 0)
                return new PlannerResult(Array.Empty<DayPlan>(), dailyLimit, workers);

            int dailyCap = workers * dailyLimit;
            var queue = new Queue<Basket>(all.OrderByDescending(b => b.NetProfit));
            var plans = new List<DayPlan>();

            DateOnly day = DateOnly.FromDateTime(DateTime.Today);
            DateOnly lastPromo = queue.SelectMany(b => b.Games).Max(g => g.PromoTo);

            while (queue.Count > 0 && day <= lastPromo)
            {
                var dp = new DayPlan(day, new List<Basket>(), new Dictionary<string, int>());
                int left = dailyCap;
                int guard = queue.Count;

                while (queue.Count > 0 && left > 0 && guard-- > 0)
                {
                    var b = queue.Peek();
                    if (b.Games.Any(g => day < g.PromoFrom || day > g.PromoTo))
                    {
                        queue.Enqueue(queue.Dequeue());
                        continue;
                    }
                    if (b.Games.Count <= left)
                    {
                        b = queue.Dequeue();
                        dp.Baskets.Add(b);
                        left -= b.Games.Count;
                        foreach (var gm in b.Games)
                        {
                            if (!dp.Items.ContainsKey(gm.Title))
                                dp.Items[gm.Title] = 0;
                            dp.Items[gm.Title]++;
                        }
                    }
                    else break;
                }
                if (dp.Baskets.Count > 0) plans.Add(dp);
                day = day.AddDays(1);
            }
            var demand = all.SelectMany(b => b.Games)
                            .GroupBy(g => g.Title)
                            .ToDictionary(g => g.Key, g => g.First().Required);

            return new PlannerResult(plans, dailyLimit, workers, demand);
        }
    }
}
