namespace MWI.Needs
{
    /// <summary>
    /// Pure-math constants and helpers for NeedSleep. Mirrors NeedHungerMath.
    /// Lives in MWI.Needs so it can be referenced from MacroSimulator (offline
    /// restoration) without dragging the full NeedSleep MonoBehaviour graph.
    /// </summary>
    public static class NeedSleepMath
    {
        public const float DEFAULT_MAX = 100f;
        public const float DEFAULT_START = 80f;
        public const float DEFAULT_LOW_THRESHOLD = 25f;

        // Decay per TimeManager phase (4 phases/day → fully drained in ~1 day awake).
        public const float DEFAULT_DECAY_PER_PHASE = 25f;

        // Live action restoration chunks (per 5s tick).
        public const float LIVE_GROUND_RESTORE_PER_TICK = 10f;
        public const float LIVE_BED_RESTORE_PER_TICK = 25f;

        // Offline (macro-sim) restoration chunks (per hour during a time skip).
        // Bed = full restore in ~2h. Ground = ~5h.
        public const float OFFLINE_BED_RESTORE_PER_HOUR = 50f;
        public const float OFFLINE_GROUND_RESTORE_PER_HOUR = 20f;
    }
}
