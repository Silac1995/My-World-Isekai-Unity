using NUnit.Framework;
using MWI.Time;

namespace MWI.Tests.LayeredTreeVisual
{
    public class TimeManagerYearProgressTests
    {
        [Test]
        public void Day1_With28DaysPerYear_ReturnsZero()
        {
            float p = TimeManager.ComputeYearProgress01(currentDay: 1, daysPerYear: 28);
            Assert.AreEqual(0f, p, 0.0001f);
        }

        [Test]
        public void Midyear_ReturnsHalf()
        {
            float p = TimeManager.ComputeYearProgress01(currentDay: 15, daysPerYear: 28);
            Assert.AreEqual(0.5f, p, 0.0001f);
        }

        [Test]
        public void EndOfYear_WrapsToZero()
        {
            float p = TimeManager.ComputeYearProgress01(currentDay: 29, daysPerYear: 28);
            Assert.AreEqual(0f, p, 0.0001f);
        }

        [Test]
        public void DaysPerYearZero_ReturnsZero_NoDivideByZero()
        {
            float p = TimeManager.ComputeYearProgress01(currentDay: 5, daysPerYear: 0);
            Assert.AreEqual(0f, p, 0.0001f);
        }

        [Test]
        public void DaysPerYearNegative_ReturnsZero()
        {
            float p = TimeManager.ComputeYearProgress01(currentDay: 5, daysPerYear: -10);
            Assert.AreEqual(0f, p, 0.0001f);
        }
    }
}
