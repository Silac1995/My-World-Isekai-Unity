using UnityEngine;
using MWI.Terrain;

namespace MWI.Weather
{
    [CreateAssetMenu(menuName = "MWI/Terrain/Biome Climate Profile")]
    public class BiomeClimateProfile : ScriptableObject
    {
        [Header("Temperature")]
        public float AmbientTemperatureMin = 5f;
        public float AmbientTemperatureMax = 25f;
        public AnimationCurve TemperatureOverDay = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Precipitation")]
        [Range(0f, 1f)] public float RainProbability = 0.3f;
        [Range(0f, 1f)] public float SnowProbability = 0.1f;
        [Range(0f, 1f)] public float CloudyProbability = 0.3f;
        public float FrontSpawnIntervalMinHours = 2f;
        public float FrontSpawnIntervalMaxHours = 8f;

        [Header("Front Properties")]
        public float FrontRadiusMin = 30f;
        public float FrontRadiusMax = 80f;
        public float FrontIntensityMin = 0.3f;
        public float FrontIntensityMax = 1.0f;
        public float FrontLifetimeMinHours = 1f;
        public float FrontLifetimeMaxHours = 6f;

        [Header("Moisture")]
        public float BaselineMoisture = 0.3f;
        public float EvaporationRate = 0.05f;

        [Header("Default Terrain")]
        public TerrainType DefaultTerrainType;
        public TerrainType DefaultFloorOnSettlement;

        public float GetAmbientTemperature(float time01)
        {
            float t = TemperatureOverDay.Evaluate(time01);
            return Mathf.Lerp(AmbientTemperatureMin, AmbientTemperatureMax, t);
        }

        private void OnValidate()
        {
            float sum = RainProbability + SnowProbability + CloudyProbability;
            if (sum > 1f)
            {
                float scale = 1f / sum;
                RainProbability *= scale;
                SnowProbability *= scale;
                CloudyProbability *= scale;
            }
        }
    }
}
