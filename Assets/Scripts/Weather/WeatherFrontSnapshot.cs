using System;
using UnityEngine;

namespace MWI.Weather
{
    [Serializable]
    public struct WeatherFrontSnapshot
    {
        public WeatherType Type;
        public Vector3 Position;
        public Vector2 LocalWindDirection;
        public float LocalWindStrength;
        public float Radius;
        public float Intensity;
        public float TemperatureModifier;
        public float RemainingLifetime;
    }
}
