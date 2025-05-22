using System.Collections.Generic;
using System.Linq;
using GiftCardBaskets.Core;

namespace GiftCardBaskets
{
    public static class Retry
    {
        public static void Do(List<Game> games, IList<Basket> baskets)
        {
            foreach (var rem in games.Where(x => x.Remaining > 0)
                                     .OrderByDescending(x => x.Remaining)
                                     .ToList())
            {
                while (rem.Remaining > 0)
                    if (!TryMakeBasket(rem, games, out var b)) break;
                    else baskets.Add(b!);
            }
        }

        private static bool TryMakeBasket(Game focus, List<Game> games, out Basket? basket)
        {
            foreach (int card in Core.Config.Cards.OrderBy(c => c).Where(c => c >= focus.Price))
            {
                var tpl = FindPack(focus, card, games);
                if (tpl == null || tpl.Count < Core.Config.MinItems) continue;

                var b = new Basket(card);
                foreach (int ix in tpl)
                {
                    var gm = games[ix];
                    b.Games.Add(gm);
                    if (gm.Remaining > 0) gm.Remaining--;
                    else gm.ExtraBought++;
                }

                if (BasketRules.MeetsRequirements(card, tpl, b.Total))
                { basket = b; return true; }
            }
            basket = null;
            return false;
        }

        private static List<int>? FindPack(Game focus, int card, List<Game> g)
        {
            var picks = new List<int> { g.IndexOf(focus) };
            decimal bestSum = 0; List<int>? best = null;

            void dfs(int idx, decimal sum)
            {
                if (sum > card || picks.Count > Core.Config.MaxItems) return;
                if (BasketRules.MeetsRequirements(card, picks, sum) && card - sum < card - bestSum)
                { bestSum = sum; best = new(picks); }

                for (int i = idx; i < g.Count; i++)
                {
                    if (picks.Contains(i)) continue;
                    var gm = g[i];
                    if (Rules.ViolatesFamilyExclusive(gm, picks.Select(p => g[p]))) continue;
                    picks.Add(i); dfs(i + 1, sum + gm.Price); picks.RemoveAt(picks.Count - 1);
                }
            }
            dfs(0, focus.Price);
            return best;
        }
    }
}
