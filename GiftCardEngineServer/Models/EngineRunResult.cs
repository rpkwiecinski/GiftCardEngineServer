using GiftCardBaskets.Core;
using System;
using System.Collections.Generic;

namespace GiftCardEngine.Models
{
    public class EngineRunResult
    {
        public EngineJobRequest Job { get; set; }
        public decimal Profit { get; set; }
        public decimal Waste { get; set; }
        public int BasketCount { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, int> StrategiesScoring { get; set; }
        public List<Basket> Baskets { get; set; }
        public Dictionary<string, StrategyStats> StrategiesStats { get; set; }
        public List<Game> Catalogue { get; set; }
        public object PlannerResult { get; set; }
        public string EngineTag { get; set; }
    }
}
