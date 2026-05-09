using NUnit.Framework;
using MWI.Jobs.Wages;

namespace MWI.Tests
{
    public class HarvesterCreditCalculatorTests
    {
        [Test]
        public void Deposit_WithinDeficit_CreditsFullDeposit()
        {
            Assert.AreEqual(3, HarvesterCreditCalculator.GetCreditedAmount(3, 5));
        }

        [Test]
        public void Deposit_ExceedsDeficit_CreditsOnlyDeficitAmount()
        {
            Assert.AreEqual(3, HarvesterCreditCalculator.GetCreditedAmount(10, 3));
        }

        [Test]
        public void Deposit_ToFullBuilding_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(5, 0));
        }

        [Test]
        public void Deposit_NegativeDeficit_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(5, -2));
        }

        [Test]
        public void Deposit_ZeroQty_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(0, 5));
        }

        [Test]
        public void Deposit_NegativeQty_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(-3, 5));
        }
    }
}
