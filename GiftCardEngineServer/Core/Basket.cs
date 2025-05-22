using System.Collections.Generic;
using System.Linq;

namespace GiftCardBaskets.Core
{
    public sealed class Basket
    {
        public int Card { get; }
        public List<Game> Games { get; } = new();

        public Basket(int card) => Card = card;

        public decimal Total => Games.Sum(g => g.Price);
        public decimal Waste => Card - Total;
        public decimal GrossProfit => Games.Sum(g => g.Price * g.ProfitPct);
        public decimal NetProfit => GrossProfit - Config.BasketFixedCost - WasteValueEur;
        public decimal WasteValueEur => Waste * Config.BrlToEur;

        public string Signature => string.Join('|', Games.Select(g => g.Title).OrderBy(t => t));
        public override bool Equals(object? o) => o is Basket b && Card == b.Card && Signature == b.Signature;
        public override int GetHashCode() => System.HashCode.Combine(Card, Signature);

        public Basket Clone()
        {
            var basket = new Basket(this.Card);
            foreach (var g in this.Games)
                basket.Games.Add(g.Clone());
            return basket;
        }
    }
}
