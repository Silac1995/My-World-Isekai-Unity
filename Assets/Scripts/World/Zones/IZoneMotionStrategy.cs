using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Pluggable motion rule for a world zone. Evaluated daily by MacroSimulator.
    /// Implementations are typically ScriptableZoneMotionStrategy SO assets so they can be editor-assigned.
    /// </summary>
    public interface IZoneMotionStrategy
    {
        /// <summary>
        /// Returns the desired XZ world delta for this zone on the given day.
        /// Return Vector3.zero for static behavior. MacroSimulator sums deltas across strategies and clamps by MapMinSeparation.
        /// </summary>
        Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay);
    }
}
