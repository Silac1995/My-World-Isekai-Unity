using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Abstract ScriptableObject base for zone-motion strategies. Editor-assignable on WildernessZone and WeatherFront.
    /// Concrete subclasses implement ComputeDailyDelta.
    /// </summary>
    public abstract class ScriptableZoneMotionStrategy : ScriptableObject, IZoneMotionStrategy
    {
        public abstract Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay);
    }
}
