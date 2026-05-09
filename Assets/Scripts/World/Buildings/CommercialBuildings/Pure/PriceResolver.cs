namespace MWI.Shop
{
    /// <summary>
    /// Pure helper resolving the effective shop price for a catalog entry.
    /// Override > 0 wins; otherwise fall back to the item's base price.
    /// Negative values clamp to zero (defensive against bad authoring).
    /// </summary>
    public static class PriceResolver
    {
        public static int Resolve(int basePrice, int priceOverride)
        {
            if (priceOverride > 0) return priceOverride;
            return basePrice > 0 ? basePrice : 0;
        }
    }
}
