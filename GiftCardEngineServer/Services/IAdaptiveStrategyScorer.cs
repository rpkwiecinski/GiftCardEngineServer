using GiftCardEngine.Models;
using System.Collections.Concurrent;

namespace GiftCardEngine.Services;

public interface IAdaptiveStrategyScorer
{
    void UpdateWithResult(EngineRunResult result);
    object GetBestStrategies();
}
