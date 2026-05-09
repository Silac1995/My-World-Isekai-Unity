using NUnit.Framework;
using MWI.Shop;

namespace MWI.Tests.Shop
{
    public class PriceResolverTests
    {
        [Test]
        public void Override_WinsOverBase_WhenOverrideIsPositive()
        {
            Assert.AreEqual(50, PriceResolver.Resolve(basePrice: 10, priceOverride: 50));
        }

        [Test]
        public void OverrideOfOne_IsRespected()
        {
            // Owner explicitly sets price = 1 (cheapest possible non-fallback)
            Assert.AreEqual(1, PriceResolver.Resolve(basePrice: 100, priceOverride: 1));
        }

        [Test]
        public void OverrideOfZero_FallsBackToBase()
        {
            Assert.AreEqual(10, PriceResolver.Resolve(basePrice: 10, priceOverride: 0));
        }

        [Test]
        public void NegativeOverride_FallsBackToBase()
        {
            Assert.AreEqual(10, PriceResolver.Resolve(basePrice: 10, priceOverride: -5));
        }

        [Test]
        public void ZeroBase_ZeroOverride_ReturnsZero()
        {
            Assert.AreEqual(0, PriceResolver.Resolve(basePrice: 0, priceOverride: 0));
        }

        [Test]
        public void NegativeBase_ClampsToZero_OnFallback()
        {
            Assert.AreEqual(0, PriceResolver.Resolve(basePrice: -10, priceOverride: 0));
        }
    }
}
