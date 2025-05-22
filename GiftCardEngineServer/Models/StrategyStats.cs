using System;

namespace GiftCardEngine.Models
{
    public class StrategyStats
    {
        public string Strategy { get; set; }
        public decimal BestProfit { get; set; }
        public decimal AvgProfit { get; set; }
        public int Runs { get; set; }
        public int SuccessRuns { get; set; }
        public decimal AvgWaste { get; set; }
        public decimal MinWaste { get; set; } = decimal.MaxValue;
        public decimal MaxWaste { get; set; } = decimal.MinValue;
    }
}
