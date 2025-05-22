namespace GiftCardBaskets.Core
{
    public static class Config
    {
        public static decimal BasketFixedCost { get; set; } = 1.00m;
        public static decimal BrlToEur { get; set; } = 0.183m;
        public static int[] Cards { get; set; } = new[] { 10, 30, 50, 100,250,300 };
        public static int MaxItems { get; set; } = 20;
        public static int MinItems { get; set; } = 1;
        public static decimal MinFillFraction { get; set; } = 0.97m;
        public static int DefaultWorkers { get; set; } = 2;
        public static int DefaultDailyCap { get; set; } = 50;
        public static decimal ExtraBuyLimitFraction { get; set; } = 1.5m;
    }
}
