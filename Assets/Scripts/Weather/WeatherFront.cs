using Unity.Netcode;
using UnityEngine;

namespace MWI.Weather
{
    public class WeatherFront : NetworkBehaviour
    {
        [Header("State")]
        public NetworkVariable<WeatherType> Type = new();
        public NetworkVariable<Vector2> LocalWindDirection = new();
        public NetworkVariable<float> LocalWindStrength = new();
        public NetworkVariable<float> Radius = new(50f);
        public NetworkVariable<float> Intensity = new(0.5f);
        public NetworkVariable<float> TemperatureModifier = new();
        public NetworkVariable<float> RemainingLifetime = new();

        private BiomeRegion _parentRegion;

        public Vector2 ActualVelocity
        {
            get
            {
                var global = GlobalWindController.Instance;
                if (global == null) return LocalWindDirection.Value * LocalWindStrength.Value;
                return (global.WindDirection.Value * global.WindStrength.Value)
                     + (LocalWindDirection.Value * LocalWindStrength.Value);
            }
        }

        public void Initialize(BiomeRegion parent, WeatherType type, Vector3 spawnPos,
            Vector2 localWind, float localWindStrength, float radius, float intensity,
            float tempModifier, float lifetime)
        {
            _parentRegion = parent;
            transform.position = spawnPos;

            if (IsServer)
            {
                Type.Value = type;
                LocalWindDirection.Value = localWind;
                LocalWindStrength.Value = localWindStrength;
                Radius.Value = radius;
                Intensity.Value = intensity;
                TemperatureModifier.Value = tempModifier;
                RemainingLifetime.Value = lifetime;
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            // Move based on combined wind (simulation time)
            Vector2 vel = ActualVelocity;
            transform.position += new Vector3(vel.x, 0f, vel.y) * UnityEngine.Time.deltaTime;

            // Decay lifetime
            RemainingLifetime.Value -= UnityEngine.Time.deltaTime;
            if (RemainingLifetime.Value <= 0f)
            {
                _parentRegion?.OnFrontExpired(this);
                NetworkObject.Despawn(true);
                return;
            }

            // Check bounds — if exited parent region, despawn
            if (_parentRegion != null)
            {
                var bounds = _parentRegion.GetComponent<BoxCollider>().bounds;
                if (!bounds.Contains(transform.position))
                {
                    _parentRegion.OnFrontExpired(this);
                    NetworkObject.Despawn(true);
                }
            }
        }

        public float GetShadowOpacity()
        {
            return Type.Value switch
            {
                WeatherType.Clear => 0f,
                WeatherType.Cloudy => 0.2f,
                WeatherType.Rain => 0.5f,
                WeatherType.Snow => 0.6f,
                _ => 0f
            };
        }
    }
}
