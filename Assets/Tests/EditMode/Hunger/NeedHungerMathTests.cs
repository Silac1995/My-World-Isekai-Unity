using MWI.Needs;
using NUnit.Framework;

namespace MWI.Tests
{
    /// <summary>
    /// Tests pure value/transition logic of NeedHungerMath — no Unity scene needed.
    /// NeedHungerMath is the dependency-free base class; NeedHunger (Assembly-CSharp)
    /// extends it with Unity/GOAP wiring. This test assembly references MWI.Hunger.Pure only.
    /// </summary>
    public class NeedHungerMathTests
    {
        [Test]
        public void StartValueClampedToMax()
        {
            var hunger = new NeedHungerMath(999f);
            Assert.AreEqual(100f, hunger.CurrentValue);
        }

        [Test]
        public void StartValueClampedToZero()
        {
            var hunger = new NeedHungerMath(-10f);
            Assert.AreEqual(0f, hunger.CurrentValue);
        }

        [Test]
        public void DecreaseValue_ReducesByAmount()
        {
            var hunger = new NeedHungerMath(80f);
            hunger.DecreaseValue(25f);
            Assert.AreEqual(55f, hunger.CurrentValue);
        }

        [Test]
        public void DecreaseValue_ClampsAtZero()
        {
            var hunger = new NeedHungerMath(10f);
            hunger.DecreaseValue(50f);
            Assert.AreEqual(0f, hunger.CurrentValue);
        }

        [Test]
        public void IncreaseValue_ClampsAtMax()
        {
            var hunger = new NeedHungerMath(90f);
            hunger.IncreaseValue(50f);
            Assert.AreEqual(100f, hunger.CurrentValue);
        }

        [Test]
        public void IsStarving_FalseWhenAboveZero()
        {
            var hunger = new NeedHungerMath(5f);
            Assert.IsFalse(hunger.IsStarving);
        }

        [Test]
        public void IsStarving_TrueWhenAtZero()
        {
            var hunger = new NeedHungerMath(0f);
            Assert.IsTrue(hunger.IsStarving);
        }

        [Test]
        public void OnStarvingChanged_FiresOnceWhenHittingZero()
        {
            var hunger = new NeedHungerMath(10f);
            int trueCount = 0, falseCount = 0;
            hunger.OnStarvingChanged += isStarving =>
            {
                if (isStarving) trueCount++; else falseCount++;
            };

            hunger.DecreaseValue(5f); // 5 — not starving yet
            Assert.AreEqual(0, trueCount);

            hunger.DecreaseValue(5f); // 0 — starving now, event must fire once
            Assert.AreEqual(1, trueCount);

            hunger.DecreaseValue(5f); // still 0 — must NOT fire again
            Assert.AreEqual(1, trueCount);

            hunger.IncreaseValue(20f); // back to 20 — must fire false once
            Assert.AreEqual(1, falseCount);

            hunger.IncreaseValue(10f); // still above 0 — must NOT fire again
            Assert.AreEqual(1, falseCount);
        }

        [Test]
        public void OnValueChanged_FiresWithNewValue()
        {
            var hunger = new NeedHungerMath(50f);
            float lastObserved = -1f;
            hunger.OnValueChanged += v => lastObserved = v;

            hunger.IncreaseValue(10f);
            Assert.AreEqual(60f, lastObserved);

            hunger.DecreaseValue(20f);
            Assert.AreEqual(40f, lastObserved);
        }

        [Test]
        public void IsLow_TrueAtOrBelowThreshold()
        {
            var hunger = new NeedHungerMath(30f);
            Assert.IsTrue(hunger.IsLow());

            hunger.IncreaseValue(1f);
            Assert.IsFalse(hunger.IsLow());
        }
    }
}
