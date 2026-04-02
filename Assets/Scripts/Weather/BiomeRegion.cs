using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;
using MWI.Terrain;
using MWI.Time;

namespace MWI.Weather
{
    [RequireComponent(typeof(BoxCollider))]
    public class BiomeRegion : MonoBehaviour, ISaveable
    {
        [SerializeField] private string _regionId;
        [SerializeField] private BiomeDefinition _biomeDefinition;
        [SerializeField] private BiomeClimateProfile _climateProfile;
        [SerializeField] private GameObject _weatherFrontPrefab;

        private List<WeatherFront> _activeFronts = new();
        private List<WeatherFrontSnapshot> _hibernatedFronts = new();
        private bool _isHibernating = true;
        private float _nextSpawnTimer;
        private BoxCollider _bounds;
        private double _lastHibernationTime;

        // --- Static Registry ---
        private static List<BiomeRegion> _allRegions = new();

        public static BiomeRegion GetRegionAtPosition(Vector3 worldPos)
        {
            foreach (var region in _allRegions)
            {
                if (region._bounds != null && region._bounds.bounds.Contains(worldPos))
                    return region;
            }
            return null;
        }

        public static List<BiomeRegion> GetAdjacentRegions(BiomeRegion region)
        {
            var result = new List<BiomeRegion>();
            if (region._bounds == null) return result;

            var expanded = region._bounds.bounds;
            expanded.Expand(20f);

            foreach (var other in _allRegions)
            {
                if (other == region) continue;
                if (other._bounds != null && expanded.Intersects(other._bounds.bounds))
                    result.Add(other);
            }
            return result;
        }

        public static void ClearRegistry()
        {
            _allRegions.Clear();
        }

        // --- Public API ---
        public string RegionId => _regionId;
        public bool IsHibernating => _isHibernating;
        public BiomeClimateProfile ClimateProfile => _climateProfile;
        public BiomeDefinition BiomeDefinition => _biomeDefinition;
        public List<WeatherFront> ActiveFronts => _activeFronts;

        public float GetAmbientTemperature()
        {
            float time01 = TimeManager.Instance != null ? TimeManager.Instance.CurrentTime01 : 0.5f;
            return _climateProfile.GetAmbientTemperature(time01);
        }

        public TerrainType GetDefaultTerrainType()
        {
            return _climateProfile != null ? _climateProfile.DefaultTerrainType : null;
        }

        public List<WeatherFront> GetFrontsOverlapping(Bounds area)
        {
            var result = new List<WeatherFront>();
            foreach (var front in _activeFronts)
            {
                if (front == null) continue;
                float dist = Vector3.Distance(front.transform.position, area.center);
                if (dist < front.Radius.Value + area.extents.magnitude)
                    result.Add(front);
            }
            return result;
        }

        // --- Lifecycle ---
        private void Awake()
        {
            _bounds = GetComponent<BoxCollider>();
            _bounds.isTrigger = true;
            _allRegions.Add(this);
        }

        private void OnDestroy()
        {
            _allRegions.Remove(this);
        }

        private void Update()
        {
            if (_isHibernating) return;
            if (!NetworkManager.Singleton.IsServer) return;

            // Spawn new fronts on timer
            _nextSpawnTimer -= UnityEngine.Time.deltaTime;
            if (_nextSpawnTimer <= 0f)
            {
                SpawnRandomFront();
                _nextSpawnTimer = UnityEngine.Random.Range(
                    _climateProfile.FrontSpawnIntervalMinHours * 3600f,
                    _climateProfile.FrontSpawnIntervalMaxHours * 3600f);
            }

            // Clean up null refs from despawned fronts
            _activeFronts.RemoveAll(f => f == null);
        }

        // --- Front Spawning ---

        private void SpawnRandomFront()
        {
            if (_weatherFrontPrefab == null || _climateProfile == null) return;

            WeatherType type = RollWeatherType();
            Vector3 spawnPos = GetRandomEdgePosition();
            Vector2 localWind = UnityEngine.Random.insideUnitCircle.normalized;
            float localWindStrength = UnityEngine.Random.Range(0.1f, 0.5f);
            float radius = UnityEngine.Random.Range(_climateProfile.FrontRadiusMin, _climateProfile.FrontRadiusMax);
            float intensity = UnityEngine.Random.Range(_climateProfile.FrontIntensityMin, _climateProfile.FrontIntensityMax);
            float lifetime = UnityEngine.Random.Range(
                _climateProfile.FrontLifetimeMinHours * 3600f,
                _climateProfile.FrontLifetimeMaxHours * 3600f);

            float tempMod = type switch
            {
                WeatherType.Rain => -3f,
                WeatherType.Snow => -10f,
                WeatherType.Clear => 2f,
                _ => -1f
            };

            try
            {
                var go = Instantiate(_weatherFrontPrefab, spawnPos, Quaternion.identity);
                var netObj = go.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn();
                    var front = go.GetComponent<WeatherFront>();
                    if (front != null)
                    {
                        front.Initialize(this, type, spawnPos, localWind, localWindStrength,
                            radius, intensity, tempMod, lifetime);
                        _activeFronts.Add(front);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BiomeRegion] Failed to spawn WeatherFront: {e.Message}");
            }
        }

        private WeatherType RollWeatherType()
        {
            float roll = UnityEngine.Random.value;
            float cumulative = 0f;

            cumulative += _climateProfile.RainProbability;
            if (roll < cumulative) return WeatherType.Rain;

            cumulative += _climateProfile.SnowProbability;
            if (roll < cumulative) return WeatherType.Snow;

            cumulative += _climateProfile.CloudyProbability;
            if (roll < cumulative) return WeatherType.Cloudy;

            return WeatherType.Clear;
        }

        private Vector3 GetRandomEdgePosition()
        {
            var b = _bounds.bounds;
            int edge = UnityEngine.Random.Range(0, 4);
            float t = UnityEngine.Random.value;

            return edge switch
            {
                0 => new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), 0f, b.min.z), // South
                1 => new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), 0f, b.max.z), // North
                2 => new Vector3(b.min.x, 0f, Mathf.Lerp(b.min.z, b.max.z, t)), // West
                3 => new Vector3(b.max.x, 0f, Mathf.Lerp(b.min.z, b.max.z, t)), // East
                _ => b.center
            };
        }

        // --- Front Expiration ---

        public void OnFrontExpired(WeatherFront front)
        {
            _activeFronts.Remove(front);
        }

        // --- Hibernation ---

        public void WakeUp(double currentTime)
        {
            if (!_isHibernating) return;
            _isHibernating = false;

            double elapsed = currentTime - _lastHibernationTime;
            float elapsedSeconds = (float)(elapsed * 24.0 * 3600.0); // Convert day-fraction to seconds

            // Fast-forward hibernated fronts
            var survivingSnapshots = new List<WeatherFrontSnapshot>();
            Vector2 globalWind = GlobalWindController.Instance != null
                ? GlobalWindController.Instance.WindDirection.Value * GlobalWindController.Instance.WindStrength.Value
                : Vector2.right * 0.3f;

            foreach (var snap in _hibernatedFronts)
            {
                var updated = snap;
                // Advance position by velocity * elapsed
                Vector2 vel = globalWind + (snap.LocalWindDirection * snap.LocalWindStrength);
                updated.Position += new Vector3(vel.x, 0f, vel.y) * elapsedSeconds;
                updated.RemainingLifetime -= elapsedSeconds;

                // Keep if still alive and in bounds
                if (updated.RemainingLifetime > 0f && _bounds.bounds.Contains(updated.Position))
                    survivingSnapshots.Add(updated);
            }

            // Respawn surviving fronts
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && _weatherFrontPrefab != null)
            {
                foreach (var snap in survivingSnapshots)
                {
                    try
                    {
                        var go = Instantiate(_weatherFrontPrefab, snap.Position, Quaternion.identity);
                        var netObj = go.GetComponent<NetworkObject>();
                        if (netObj != null)
                        {
                            netObj.Spawn();
                            var front = go.GetComponent<WeatherFront>();
                            if (front != null)
                            {
                                front.Initialize(this, snap.Type, snap.Position, snap.LocalWindDirection,
                                    snap.LocalWindStrength, snap.Radius, snap.Intensity,
                                    snap.TemperatureModifier, snap.RemainingLifetime);
                                _activeFronts.Add(front);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[BiomeRegion] Failed to respawn front on wake: {e.Message}");
                    }
                }

                // Spawn new fronts that would have appeared during hibernation
                float spawnInterval = (_climateProfile.FrontSpawnIntervalMinHours + _climateProfile.FrontSpawnIntervalMaxHours) / 2f * 3600f;
                int newFrontsToSpawn = Mathf.FloorToInt(elapsedSeconds / spawnInterval);
                for (int i = 0; i < newFrontsToSpawn; i++)
                    SpawnRandomFront();
            }

            _hibernatedFronts.Clear();
            _nextSpawnTimer = UnityEngine.Random.Range(
                _climateProfile.FrontSpawnIntervalMinHours * 3600f,
                _climateProfile.FrontSpawnIntervalMaxHours * 3600f);

            Debug.Log($"[BiomeRegion] '{_regionId}' woke up after {elapsed:F2} days. " +
                      $"Restored {survivingSnapshots.Count} fronts, spawned {Mathf.FloorToInt(elapsedSeconds / ((_climateProfile.FrontSpawnIntervalMinHours + _climateProfile.FrontSpawnIntervalMaxHours) / 2f * 3600f))} new.");
        }

        public void Hibernate(double currentTime)
        {
            if (_isHibernating) return;
            _isHibernating = true;
            _lastHibernationTime = currentTime;

            _hibernatedFronts.Clear();
            foreach (var front in _activeFronts)
            {
                if (front == null) continue;
                _hibernatedFronts.Add(new WeatherFrontSnapshot
                {
                    Type = front.Type.Value,
                    Position = front.transform.position,
                    LocalWindDirection = front.LocalWindDirection.Value,
                    LocalWindStrength = front.LocalWindStrength.Value,
                    Radius = front.Radius.Value,
                    Intensity = front.Intensity.Value,
                    TemperatureModifier = front.TemperatureModifier.Value,
                    RemainingLifetime = front.RemainingLifetime.Value
                });
                front.NetworkObject.Despawn(true);
            }
            _activeFronts.Clear();

            Debug.Log($"[BiomeRegion] '{_regionId}' hibernated. Serialized {_hibernatedFronts.Count} fronts.");
        }

        // --- ISaveable ---
        public string SaveKey => _regionId;

        public object CaptureState()
        {
            return new BiomeRegionSaveData
            {
                RegionId = _regionId,
                IsHibernating = _isHibernating,
                LastHibernationTime = _lastHibernationTime,
                HibernatedFronts = _hibernatedFronts.ToArray()
            };
        }

        public void RestoreState(object state)
        {
            if (state is BiomeRegionSaveData data)
            {
                _isHibernating = data.IsHibernating;
                _lastHibernationTime = data.LastHibernationTime;
                _hibernatedFronts = data.HibernatedFronts?.ToList() ?? new List<WeatherFrontSnapshot>();
            }
        }
    }

    [Serializable]
    public class BiomeRegionSaveData
    {
        public string RegionId;
        public bool IsHibernating;
        public double LastHibernationTime;
        public WeatherFrontSnapshot[] HibernatedFronts;
    }
}
