// Assets/Scripts/Terrain/TerrainWeatherProcessor.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using MWI.Weather;

namespace MWI.Terrain
{
    public class TerrainWeatherProcessor : MonoBehaviour
    {
        [SerializeField] private float _tickIntervalGameMinutes = 2f;
        [SerializeField] private List<TerrainTransitionRule> _transitionRules;
        [SerializeField] private float _rainMoistureRate = 0.1f;
        [SerializeField] private float _snowAccumulationRate = 0.05f;

        private TerrainCellGrid _grid;
        private BiomeRegion _biomeRegion;
        private float _timeSinceLastTick;
        private HashSet<int> _dirtyCells = new();

        public event Action<int, int, TerrainType> OnCellTerrainChanged;

        public void Initialize(TerrainCellGrid grid, BiomeRegion region)
        {
            _grid = grid;
            _biomeRegion = region;
            if (_transitionRules != null)
                _transitionRules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        private void Update()
        {
            if (_grid == null || _biomeRegion == null) return;
            if (!NetworkManager.Singleton.IsServer) return;

            _timeSinceLastTick += UnityEngine.Time.deltaTime;

            float tickInterval = _tickIntervalGameMinutes * 60f;
            while (_timeSinceLastTick >= tickInterval)
            {
                _timeSinceLastTick -= tickInterval;
                ProcessTick(tickInterval);
            }
        }

        private void ProcessTick(float tickDelta)
        {
            var mapCollider = GetComponent<BoxCollider>();
            if (mapCollider == null) return;

            var overlappingFronts = _biomeRegion.GetFrontsOverlapping(mapCollider.bounds);

            if (overlappingFronts.Count > 0)
                ProcessWeatherFronts(overlappingFronts, tickDelta);

            ProcessAmbientRevert(tickDelta);
            EvaluateTransitions();
        }

        private void ProcessWeatherFronts(List<WeatherFront> fronts, float tickDelta)
        {
            foreach (var front in fronts)
            {
                if (front == null) continue;

                var frontBounds = new Bounds(front.transform.position,
                    Vector3.one * front.Radius.Value * 2f);
                _grid.GetCellRangeForBounds(frontBounds,
                    out int minX, out int minZ, out int maxX, out int maxZ);

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector3 cellWorld = _grid.GridToWorld(x, z);
                        float dist = Vector3.Distance(cellWorld, front.transform.position);
                        if (dist > front.Radius.Value) continue;

                        float falloff = 1f - (dist / front.Radius.Value);
                        Vector2 cellOffset = new Vector2(
                            cellWorld.x - front.transform.position.x,
                            cellWorld.z - front.transform.position.z).normalized;
                        float windBias = Vector2.Dot(front.ActualVelocity.normalized, cellOffset);
                        float contribution = falloff * (1f + windBias * 0.3f) * front.Intensity.Value;

                        ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                        int idx = z * _grid.Width + x;

                        if (front.Type.Value == WeatherType.Rain)
                        {
                            cell.Moisture = Mathf.Clamp01(
                                cell.Moisture + contribution * _rainMoistureRate * tickDelta);
                            cell.TimeSinceLastWatered = 0f;
                        }
                        else if (front.Type.Value == WeatherType.Snow)
                        {
                            cell.SnowDepth = Mathf.Clamp01(
                                cell.SnowDepth + contribution * _snowAccumulationRate * tickDelta);
                        }

                        cell.Temperature += front.TemperatureModifier.Value * falloff * tickDelta * 0.1f;
                        _dirtyCells.Add(idx);
                    }
                }
            }
        }

        private void ProcessAmbientRevert(float tickDelta)
        {
            if (_dirtyCells.Count == 0) return;

            var profile = _biomeRegion.ClimateProfile;
            float ambientTemp = _biomeRegion.GetAmbientTemperature();
            float windFactor = GlobalWindController.Instance != null
                ? 1f + GlobalWindController.Instance.WindStrength.Value
                : 1f;

            var toRemove = new List<int>();

            foreach (int idx in _dirtyCells)
            {
                int x = idx % _grid.Width;
                int z = idx / _grid.Width;
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);

                cell.Moisture -= profile.EvaporationRate * windFactor * tickDelta * 0.01f;
                cell.Moisture = Mathf.MoveTowards(cell.Moisture, profile.BaselineMoisture, 0.001f * tickDelta);
                cell.Moisture = Mathf.Clamp01(cell.Moisture);

                cell.Temperature = Mathf.MoveTowards(cell.Temperature, ambientTemp, 0.5f * tickDelta);

                if (cell.SnowDepth > 0f && cell.Temperature > 0f)
                    cell.SnowDepth = Mathf.Max(0f, cell.SnowDepth - 0.01f * cell.Temperature * tickDelta);

                cell.TimeSinceLastWatered += tickDelta / 3600f;

                bool atBaseline = Mathf.Abs(cell.Moisture - profile.BaselineMoisture) < 0.01f
                    && Mathf.Abs(cell.Temperature - ambientTemp) < 0.5f
                    && cell.SnowDepth <= 0f;
                if (atBaseline) toRemove.Add(idx);
            }

            foreach (int idx in toRemove)
                _dirtyCells.Remove(idx);
        }

        private void EvaluateTransitions()
        {
            if (_transitionRules == null || _transitionRules.Count == 0) return;

            foreach (int idx in _dirtyCells)
            {
                int x = idx % _grid.Width;
                int z = idx / _grid.Width;
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);

                string previousTypeId = cell.CurrentTypeId;
                bool matched = false;

                foreach (var rule in _transitionRules)
                {
                    if (rule.SourceType.TypeId != cell.CurrentTypeId
                        && rule.SourceType.TypeId != cell.BaseTypeId) continue;

                    if (rule.Evaluate(cell.Moisture, cell.Temperature, cell.SnowDepth))
                    {
                        cell.CurrentTypeId = rule.ResultType.TypeId;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                    cell.CurrentTypeId = cell.BaseTypeId;

                if (cell.CurrentTypeId != previousTypeId)
                {
                    var newType = TerrainTypeRegistry.Get(cell.CurrentTypeId);
                    OnCellTerrainChanged?.Invoke(x, z, newType);
                }
            }
        }
    }
}
