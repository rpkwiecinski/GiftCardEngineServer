using GiftCardEngine.Models;

public sealed class EngineTrainerResultsHolder
{
    private EngineRunResult? _result;
    private readonly object _lock = new();

    public void SetResult(EngineRunResult result)
    {
        lock (_lock)
        {
            _result = result;
        }
    }

    public EngineRunResult? GetResult()
    {
        lock (_lock)
        {
            return _result;
        }
    }
}
