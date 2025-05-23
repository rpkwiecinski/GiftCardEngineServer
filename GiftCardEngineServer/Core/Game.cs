﻿using System;
using System.Text.Json.Serialization;

namespace GiftCardBaskets.Core
{
    public sealed class Game
    {
        public string Title { get; }
        public decimal Price { get; }
        public int Required { get; }
        public int Remaining;
        public int ExtraBought;
        public decimal ProfitPct { get; }
        public DateOnly PromoFrom { get; }
        public DateOnly PromoTo { get; }
        public int Rating { get; }
        public string Family { get; }
        public bool FamilyExclusive { get; }
        public int MaxAllowed { get; set; }

        [JsonConstructor]
        public Game(
            string title,
            decimal price,
            int required,
            decimal profitPct,
            DateOnly promoFrom,
            DateOnly promoTo,
            int rating,
            string? family = null,
            bool familyExclusive = false)
        {
            Title = title;
            Price = price;
            Required = Remaining = required;
            ProfitPct = profitPct;
            PromoFrom = promoFrom;
            PromoTo = promoTo;
            Rating = rating;
            Family = family ?? string.Empty;
            FamilyExclusive = familyExclusive;
            MaxAllowed = (int)Math.Round(required * Config.ExtraBuyLimitFraction);
        }

        public Game Clone() => new(Title, Price, Required, ProfitPct, PromoFrom, PromoTo, Rating, Family, FamilyExclusive)
        { Remaining = Remaining, ExtraBought = ExtraBought, MaxAllowed = MaxAllowed };
    }
}
