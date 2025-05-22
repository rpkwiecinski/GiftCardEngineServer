namespace GiftCardEngine.Models;

public class EngineRunResult
{
    public EngineJobRequest Job { get; set; }
    public decimal Profit { get; set; }
    public decimal Waste { get; set; }
    public int BasketCount { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, int> StrategiesScoring { get; set; }
}
