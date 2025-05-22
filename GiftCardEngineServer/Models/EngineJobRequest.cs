namespace GiftCardEngine.Models;

public class EngineJobRequest
{
    public string JobName { get; set; }
    public string CataloguePath { get; set; } // path to .json, .csv etc.
    public int Workers { get; set; }
    public int DailyLimit { get; set; }
}
