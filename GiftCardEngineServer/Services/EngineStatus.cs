using GiftCardEngine.Models;
using System;
using System.Collections.Generic;

public class EngineStatus
{
    public bool Running { get; set; }
    public int Queue { get; set; }
    public int JobsDone { get; set; }
    public object BestStrategies { get; set; } // może być np. List<string> lub inny typ zależnie od Twojej logiki
    public Dictionary<string, StrategyStats> StrategiesStats { get; set; }
    public DateTime? LastRun { get; set; }
    public EngineRunResult? LastJobResult { get; set; }
}

public class StrategyAggregateInfo
{
    public int Runs { get; set; }
    public int SuccessRuns { get; set; }
    public decimal AvgProfit { get; set; }
    public decimal BestProfit { get; set; }
}
