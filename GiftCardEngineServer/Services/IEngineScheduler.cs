using GiftCardEngine.Models;
using GiftCardBaskets.Engines;
using System.Collections.Concurrent;

namespace GiftCardEngine.Services;

public interface IEngineScheduler
{
    Task ProcessQueueAsync(CancellationToken cancellationToken);

    Task<EngineJobResult> RunJobAsync(EngineJobRequest req);
    object GetStatus();
}
