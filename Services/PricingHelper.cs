using ASM_1.Models.Food;
using System;

namespace ASM_1.Services
{
    public static class PricingHelper
    {
        public static decimal CalculateEffectiveBasePrice(FoodItem item)
        {
            if (item == null) return 0m;

            if (item.DiscountPrice > 0)
            {
                return decimal.Round(item.DiscountPrice, 0, MidpointRounding.AwayFromZero);
            }

            if (item.DiscountPercent > 0)
            {
                var percent = Math.Clamp(item.DiscountPercent, 0, 100);
                var discounted = item.BasePrice * (100 - percent) / 100m;
                return decimal.Round(discounted, 0, MidpointRounding.AwayFromZero);
            }

            return decimal.Round(item.BasePrice, 0, MidpointRounding.AwayFromZero);
        }

        public static decimal CalculateComboPrice(Combo combo)
        {
            if (combo.ComboDetails == null || combo.ComboDetails.Count == 0)
            {
                return 0m;
            }

            decimal total = 0m;
            foreach (var detail in combo.ComboDetails)
            {
                if (detail.FoodItem == null) continue;
                total += CalculateEffectiveBasePrice(detail.FoodItem) * detail.Quantity;
            }

            if (combo.DiscountPercentage.HasValue && combo.DiscountPercentage.Value > 0)
            {
                var percent = Math.Clamp(combo.DiscountPercentage.Value, 0, 100);
                total = total * (100 - percent) / 100m;
            }

            return decimal.Round(total, 0, MidpointRounding.AwayFromZero);
        }
    }
}
