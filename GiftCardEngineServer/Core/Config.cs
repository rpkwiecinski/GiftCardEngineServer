namespace GiftCardBaskets.Core
{
    public static class Config
    {
        public static decimal BasketFixedCost { get; set; } = 0.50m;
        public static decimal BrlToEur { get; set; } = 0.183m;
        public static int[] Cards { get; set; } = new[] { 10, 30, 50, 100 };
        public static int MaxItems { get; set; } = 6;
        public static int MinItems { get; set; } = 1;
        public static decimal MinFillFraction { get; set; } = 0.97m;
        public static int DefaultWorkers { get; set; } = 3;
        public static int DefaultDailyCap { get; set; } = 150;
        public static decimal ExtraBuyLimitFraction { get; set; } = 1.5m;
    }
}
