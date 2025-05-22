using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GiftCardBaskets
{
    public static class BasketRules
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MeetsRequirements(int card, IReadOnlyList<int> picks, decimal sum) =>
            picks.Count >= GiftCardBaskets.Core.Config.MinItems && sum >= card * GiftCardBaskets.Core.Config.MinFillFraction;
    }
}
