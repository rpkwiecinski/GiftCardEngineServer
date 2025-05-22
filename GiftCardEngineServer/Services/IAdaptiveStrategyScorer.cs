using GiftCardEngine.Models;
using System.Collections.Generic;

namespace GiftCardEngine.Services
{
    public interface IAdaptiveStrategyScorer
    {
        void UpdateWithResult(EngineRunResult result);
        List<KeyValuePair<string, int>> GetBestStrategies();
    }
}
