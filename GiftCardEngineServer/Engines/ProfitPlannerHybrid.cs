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
        private HybridEngineMemory _memory = new HybridEngineMemory();
        private readonly string _statsPath = "history/strategy_stats.json";

        // --- Configurable RL parameters ---
        private readonly int _iterations = 1500;                   // liczba powtórzeń (możesz zmienić przez konstruktor)
        private readonly double _epsilonStart = 0.4;               // startowe epsilon (eksploracja)
        private readonly double _epsilonDecay = 0.995;             // tempo zaniku epsilonu
        private readonly double _epsilonMin = 0.01;                // minimum eksploracji
        private readonly bool _useUcb1 = true;                     // tryb UCB1 (Upper Confidence Bound)
        private readonly int _generations = 2;                     // ile pokoleń genetycznych generować
        private readonly int _offspringPerGen = 12;                // ile dzieci/potomków na pokolenie
        private readonly int _eliteTake = 7;                       // ile najlepszych koszyków zachować do genetyki
        private readonly int _rollingWindow = 1000;                // rozmiar rolling window do statystyk

        public ProfitPlannerHybrid()
        {
            Directory.CreateDirectory("history");
            _memory.Load(_statsPath);
        }

        public Dictionary<string, int> GetStrategyScores() => _localStrategyScores;

        public async Task<List<Basket>> BuildAsync(
            List<Game> catalogue,
            int workers,
            int dailyLimit,
            CancellationToken ct = default)
        {
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

            // RL session, parametryzowana pętla
            double epsilon = _epsilonStart;
            for (int iter = 0; iter < _iterations && !ct.IsCancellationRequested; iter++)
            {
                // --- Epsilon decay (dynamiczna eksploracja/eksploatacja) ---
                epsilon = Math.Max(_epsilonMin, _epsilonStart * Math.Pow(_epsilonDecay, iter));

                int stratIdx;
                if (_useUcb1)
                {
                    // --- UCB1 Selection ---
                    var allStats = _memory.GetAllStats();
                    double totalRuns = allStats.Sum(x => (double)x.Value.Runs) + 1.0;
                    double c = 2.0; // tunable
                    double bestUcb = double.MinValue;
                    int bestIdx = 0;
                    for (int i = 0; i < stratCount; i++)
                    {
                        var name = stratNames[i];
                        var s = allStats.TryGetValue(name, out var stat) ? stat : null;
                        double mean = s != null && s.Runs > 0 ? (double)s.AvgProfit : 0.0;
                        double bonus = s != null && s.Runs > 0 ? c * Math.Sqrt(Math.Log(totalRuns) / s.Runs) : 1.0;
                        double ucb = mean + bonus;
                        if (rand.NextDouble() < epsilon) // eksploracja na siłę
                            ucb += rand.NextDouble() * 2.0;
                        if (ucb > bestUcb)
                        {
                            bestUcb = ucb;
                            bestIdx = i;
                        }
                    }
                    stratIdx = bestIdx;
                }
                else
                {
                    // --- Epsilon-greedy selection ---
                    if (rand.NextDouble() < epsilon)
                    {
                        stratIdx = rand.Next(stratCount); // eksploracja
                    }
                    else
                    {
                        // eksploatacja: najlepsza wg statystyk
                        var allStats = _memory.GetAllStats();
                        decimal bestAvg = decimal.MinValue;
                        int bestIdx = 0;
                        for (int i = 0; i < stratCount; i++)
                        {
                            var name = stratNames[i];
                            decimal avg = allStats.TryGetValue(name, out var stat) ? stat.AvgProfit : 0m;
                            if (avg > bestAvg)
                            {
                                bestAvg = avg;
                                bestIdx = i;
                            }
                        }
                        stratIdx = bestIdx;
                    }
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

                // RL: aktualizacja pamięci/statystyk (rolling window)
                string strategy = stratNames[stratIdx];
                bool success = profit > 0;
                _memory.Update(strategy, profit, success);
                _memory.TrimStats(_rollingWindow);
            }

            // Zapisz statystyki RL na końcu sesji
            _memory.Save(_statsPath);

            Plan = BuildCalendar(best ?? new List<Basket>(), workers, dailyLimit);
            LogAdaptiveSessionResults(stratNames, stratUsage, runLog, bestProfit);

            _localStrategyScores = new Dictionary<string, int>();
            for (int i = 0; i < stratNames.Length; i++)
                _localStrategyScores[stratNames[i]] = stratScore[i];

            // --- Genetyka: kilka pokoleń i offspringów ---
            var allElites = new List<Basket>();
            if (stratResults.Count > 0)
            {
                allElites = stratResults
                    .OrderByDescending(x => x.profit)
                    .Take(_eliteTake)
                    .SelectMany(x => x.baskets)
                    .Distinct()
                    .Take(_eliteTake * 2)
                    .ToList();
            }

            for (int gen = 0; gen < _generations; gen++)
            {
                var mutants = EvolveBaskets(allElites, catalogue, rand, _offspringPerGen);
                foreach (var mutant in mutants)
                {
                    decimal mutantProfit = mutant.NetProfit;
                    decimal mutantWaste = mutant.WasteValueEur;
                    _memory.Update("EVOLVED", mutantProfit, mutantProfit > 0);

                    if (mutantProfit > bestProfit)
                    {
                        bestProfit = mutantProfit;
                        best = new List<Basket> { mutant.Clone() };
                    }
                }
                allElites.AddRange(mutants);
                allElites = allElites.Distinct().OrderByDescending(b => b.NetProfit).Take(_eliteTake * 2).ToList();
            }

            LogAdaptiveSessionResults(stratNames, stratUsage, runLog, bestProfit);

            _localStrategyScores = new Dictionary<string, int>();
            for (int i = 0; i < stratNames.Length; i++)
                _localStrategyScores[stratNames[i]] = stratScore[i];

            return best ?? new List<Basket>();
        }

        private List<Basket> EvolveBaskets(List<Basket> elite, List<Game> catalogue, Random rand, int offspringCount = 10)
        {
            var offspring = new List<Basket>();

            // Mutacje - zamiana jednej gry na inną w koszyku
            foreach (var parent in elite)
            {
                for (int m = 0; m < 2; m++) // dwa mutanty na koszyk
                {
                    var mutant = parent.Clone();
                    if (mutant.Games.Count == 0) continue;
                    int idx = rand.Next(mutant.Games.Count);
                    var candidate = catalogue
                        .Where(g => g.Price <= (mutant.Card - (mutant.Games.Sum(x => x.Price) - mutant.Games[idx].Price))
                                    && g.Title != mutant.Games[idx].Title)
                        .OrderBy(_ => rand.Next())
                        .FirstOrDefault();
                    if (candidate != null)
                    {
                        mutant.Games[idx] = candidate.Clone();
                        offspring.Add(mutant);
                    }
                }
            }

            // Krzyżowanie - mieszanie dwóch koszyków
            for (int c = 0; c < offspringCount; c++)
            {
                var p1 = elite[rand.Next(elite.Count)];
                var p2 = elite[rand.Next(elite.Count)];
                var child = new Basket(p1.Card);
                int half = Math.Min(p1.Games.Count, p2.Games.Count) / 2;
                child.Games.AddRange(p1.Games.Take(half).Select(g => g.Clone()));
                child.Games.AddRange(p2.Games.Skip(half).Take(child.Card - child.Games.Sum(g => g.Price) > 0 ? p2.Games.Count - half : 0).Select(g => g.Clone()));
                offspring.Add(child);
            }
            return offspring;
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
