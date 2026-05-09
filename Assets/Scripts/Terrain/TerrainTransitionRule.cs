using UnityEngine;

namespace MWI.Terrain
{
    [CreateAssetMenu(menuName = "MWI/Terrain/Transition Rule")]
    public class TerrainTransitionRule : ScriptableObject
    {
        public TerrainType SourceType;
        public TerrainType ResultType;
        [SerializeField] private int _priority = 0;

        [Header("Conditions (ALL must be met)")]
        [Tooltip("-1 = don't check")]
        public float MinMoisture = -1f;
        public float MaxMoisture = -1f;
        [Tooltip("-999 = don't check")]
        public float MinTemperature = -999f;
        public float MaxTemperature = 999f;
        public float MinSnowDepth = -1f;

        public int Priority => _priority;

        public bool Evaluate(float moisture, float temperature, float snowDepth)
        {
            if (MinMoisture >= 0 && moisture < MinMoisture) return false;
            if (MaxMoisture >= 0 && moisture > MaxMoisture) return false;
            if (temperature < MinTemperature || temperature > MaxTemperature) return false;
            if (MinSnowDepth >= 0 && snowDepth < MinSnowDepth) return false;
            return true;
        }
    }
}
