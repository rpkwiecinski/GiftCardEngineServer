using GiftCardBaskets.Core;

namespace GiftCardBaskets.Engines
{
    public interface IScheduleProvider
    {
        PlannerResult Plan { get; }
    }
}
