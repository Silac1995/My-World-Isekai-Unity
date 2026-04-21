using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Default motion strategy — zero daily delta. Zones with this strategy (and no others) never move.
    /// </summary>
    [CreateAssetMenu(fileName = "StaticMotion", menuName = "MWI/World/Motion/Static", order = 0)]
    public class StaticMotionStrategy : ScriptableZoneMotionStrategy
    {
        public override Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay) => Vector3.zero;
    }
}
