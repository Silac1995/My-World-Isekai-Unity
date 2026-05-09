namespace MWI.Needs
{
    /// <summary>
    /// Pure static math for hunger offline catch-up (macro-simulation hibernation).
    /// Lives in MWI.Hunger.Pure so EditMode tests can exercise it without depending
    /// on Assembly-CSharp or MacroSimulator.
    /// </summary>
    public static class HungerCatchUpMath
    {
        /// <summary>
        /// Returns the new hunger value after <paramref name="hoursPassed"/> hours of linear
        /// decay at <paramref name="decayPerHour"/> units/hour, clamped to [0, ∞).
        /// </summary>
        /// <param name="currentValue">Hunger value at the start of hibernation.</param>
        /// <param name="decayPerHour">Drain rate in hunger units per in-game hour.</param>
        /// <param name="hoursPassed">In-game hours elapsed while hibernated.</param>
        /// <returns>New hunger value, never below 0.</returns>
        public static float ApplyDecay(float currentValue, float decayPerHour, float hoursPassed)
        {
            if (hoursPassed <= 0f) return currentValue;
            float result = currentValue - decayPerHour * hoursPassed;
            return result < 0f ? 0f : result;
        }
    }
}
