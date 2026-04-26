using MWI.Needs;
using NUnit.Framework;

namespace MWI.Tests
{
    /// <summary>
    /// Tests the hunger offline catch-up math used by MacroSimulator during NPC hibernation.
    /// Uses HungerCatchUpMath (MWI.Hunger.Pure) — no Assembly-CSharp / MacroSimulator dependency needed.
    /// Decay rate: NeedHunger drains 25 per phase x 4 phases/day = 100/day = 100/24 per hour.
    /// </summary>
    public class MacroSimulatorHungerTests
    {
        // 25 per phase * 4 phases per day = 100 / 24 hours
        private const float DECAY_PER_HOUR = 100f / 24f;

        [Test]
        public void NoTimePassed_ReturnsCurrent()
        {
            float result = HungerCatchUpMath.ApplyDecay(80f, DECAY_PER_HOUR, 0f);
            Assert.AreEqual(80f, result, 0.01f);
        }

        [Test]
        public void OneFullDay_DecaysBy100ClampedToZero()
        {
            float result = HungerCatchUpMath.ApplyDecay(80f, DECAY_PER_HOUR, 24f);
            Assert.AreEqual(0f, result, 0.01f);
        }

        [Test]
        public void HalfDay_DecaysBy50()
        {
            float result = HungerCatchUpMath.ApplyDecay(80f, DECAY_PER_HOUR, 12f);
            Assert.AreEqual(30f, result, 0.01f);
        }

        [Test]
        public void NegativeOrZeroResult_ClampsToZero()
        {
            float result = HungerCatchUpMath.ApplyDecay(10f, DECAY_PER_HOUR, 24f);
            Assert.AreEqual(0f, result, 0.01f);
        }
    }
}
