using GiftCardBaskets.Core;

namespace GiftCardBaskets.Engines
{
    public interface IEngine
    {
        string Name { get; }
        Task<List<Basket>> BuildAsync(List<Game> catalogue, int workers, int dailyLimit, CancellationToken ct = default);
    }
}
