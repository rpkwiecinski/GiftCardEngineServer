using System.Collections.Concurrent;
using System.Text.Json;
using GiftCardEngine.Models;

namespace GiftCardEngine.Services;

public interface IResultRepository
{
    void SaveResult(EngineRunResult result);
    IEnumerable<EngineRunResult> GetLastResults();
}