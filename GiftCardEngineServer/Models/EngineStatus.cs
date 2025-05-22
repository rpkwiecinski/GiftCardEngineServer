using System;
using System.Collections.Generic;

namespace GiftCardEngine.Models
{
    public class EngineStatus
    {
        public bool Running { get; set; }
        public int Queue { get; set; }
        public int JobsDone { get; set; }
        public object BestStrategies { get; set; }
        public Dictionary<string, StrategyStats> StrategiesStats { get; set; }
        public DateTime? LastRun { get; set; }
        public EngineRunResult? LastJobResult { get; set; }
    }
}
