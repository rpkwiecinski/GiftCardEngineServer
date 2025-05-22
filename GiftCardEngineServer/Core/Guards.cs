using System.Collections.Generic;
using System.Linq;
using GiftCardBaskets.Core;

namespace GiftCardBaskets
{
    public static class Guards
    {
        public static bool CanAddGamesToBasket(List<int> picks, List<Game> games)
        {
            var grouped = picks.GroupBy(ix => ix)
                .Select(gr => new { ix = gr.Key, cnt = gr.Count() });

            foreach (var gr in grouped)
            {
                var g = games[gr.ix];
                if (g.ExtraBought + gr.cnt > g.MaxAllowed - g.Required)
                    return false;
            }
            return true;
        }
    }
}
