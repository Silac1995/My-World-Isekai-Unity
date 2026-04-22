using NUnit.Framework;
using MWI.Jobs.Wages;

namespace MWI.Tests
{
    public class WageCalculatorTests
    {
        [Test]
        public void FullShiftZeroUnits_PieceWork_PaysFullMinimum()
        {
            // 8h/8h, min=10, piece=2, units=0 -> ratio=1, wage=10 + 0 = 10
            int w = WageCalculator.ComputePieceWorkWage(8f, 8f, 10, 2, 0);
            Assert.AreEqual(10, w);
        }

        [Test]
        public void FullShiftWithUnits_PieceWork_AddsPieceBonus()
        {
            // 8h/8h, min=10, piece=2, units=5 -> 10 + 10 = 20
            int w = WageCalculator.ComputePieceWorkWage(8f, 8f, 10, 2, 5);
            Assert.AreEqual(20, w);
        }

        [Test]
        public void HalfShift_PieceWork_ProratesMinimumOnly()
        {
            // 4h/8h, min=10, piece=2, units=3 -> ratio=0.5, wage=5 + 6 = 11
            int w = WageCalculator.ComputePieceWorkWage(4f, 8f, 10, 2, 3);
            Assert.AreEqual(11, w);
        }

        [Test]
        public void OvertimeHours_PieceWork_CapsRatioAtOne()
        {
            // 12h/8h, min=10, piece=2, units=0 -> ratio=1.0 (not 1.5), wage=10
            int w = WageCalculator.ComputePieceWorkWage(12f, 8f, 10, 2, 0);
            Assert.AreEqual(10, w);
        }

        [Test]
        public void FullShift_FixedWage_PaysFull()
        {
            int w = WageCalculator.ComputeFixedWage(8f, 8f, 15);
            Assert.AreEqual(15, w);
        }

        [Test]
        public void HalfShift_FixedWage_ProratesHalf()
        {
            int w = WageCalculator.ComputeFixedWage(4f, 8f, 15);
            Assert.AreEqual(8, w); // 0.5 * 15 = 7.5 -> banker's rounding to nearest even = 8
        }

        [Test]
        public void ZeroScheduledHours_ReturnsZeroRatio()
        {
            int w = WageCalculator.ComputePieceWorkWage(1f, 0f, 10, 2, 5);
            // ratio clamps to 0, wage = 0 + 10 = 10 (piece still pays)
            Assert.AreEqual(10, w);
        }

        [Test]
        public void NegativeHours_ReturnsZeroRatio()
        {
            int w = WageCalculator.ComputeFixedWage(-2f, 8f, 15);
            Assert.AreEqual(0, w);
        }
    }
}
