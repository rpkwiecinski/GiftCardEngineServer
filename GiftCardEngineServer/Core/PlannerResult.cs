using System.Collections.Generic;

namespace GiftCardBaskets.Core
{
    public class PlannerResult
    {
        public IReadOnlyList<DayPlan> Calendar { get; }
        public int DailyLimit { get; }
        public int Workers { get; }
        public Dictionary<string, int> Demand { get; }

        public PlannerResult(IReadOnlyList<DayPlan> calendar, int dailyLimit, int workers, Dictionary<string, int> demand = null)
        {
            Calendar = calendar;
            DailyLimit = dailyLimit;
            Workers = workers;
            Demand = demand ?? new Dictionary<string, int>();
        }
    }
}
