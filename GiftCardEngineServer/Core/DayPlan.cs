using System;
using System.Collections.Generic;

namespace GiftCardBaskets.Core
{
    public class DayPlan
    {
        public DateOnly Day { get; }
        public List<Basket> Baskets { get; }
        public Dictionary<string, int> Items { get; }

        public DayPlan(DateOnly day, List<Basket> baskets, Dictionary<string, int> items)
        {
            Day = day;
            Baskets = baskets;
            Items = items;
        }
    }
}
