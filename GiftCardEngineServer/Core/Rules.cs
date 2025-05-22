using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GiftCardBaskets
{
    public static class Rules
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ViolatesFamilyExclusive(GiftCardBaskets.Core.Game cand, IEnumerable<GiftCardBaskets.Core.Game> current) =>
            !string.IsNullOrEmpty(cand.Family) &&
            current.Any(p => p.Family == cand.Family &&
                            (p.FamilyExclusive || cand.FamilyExclusive));
    }
}
