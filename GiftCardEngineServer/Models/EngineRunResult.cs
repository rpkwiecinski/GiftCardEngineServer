using System;
using System.Collections.Generic;
using GiftCardBaskets.Core; // lub odpowiednia przestrzeń dla Game/Basket/PlannerResult

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

        // Dodane na potrzeby szczegółowych logów / raportów
        public string EngineTag { get; set; }
        public List<Game> Catalogue { get; set; } = new();
        public List<Basket> Baskets { get; set; } = new();
        public PlannerResult PlannerResult { get; set; }
    }
}
