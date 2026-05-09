using System;
using UnityEngine;
using MWI.Terrain;
using MWI.Weather;
using MWI.WorldSystem;

/// <summary>
/// Character subsystem that detects terrain under feet and applies effects.
/// Runs on BOTH server and client — server applies gameplay effects,
/// client reads CurrentTerrainType for audio/VFX.
/// </summary>
public class CharacterTerrainEffects : CharacterSystem
{
    public TerrainType CurrentTerrainType { get; private set; }
    public bool IsInWeatherFront { get; private set; }
    public WeatherType CurrentWeather { get; private set; }

    public event Action<TerrainType> OnTerrainChanged;

    private TerrainType _lastTerrainType;
    private bool _isDead;

    protected override void HandleDeath(Character character)
    {
        _isDead = true;
    }

    protected override void HandleWakeUp(Character character)
    {
        _isDead = false;
    }

    private void Update()
    {
        if (_isDead) return;

        UpdateTerrainDetection();

        if (IsServer)
            ApplyTerrainEffects();
    }

    private void UpdateTerrainDetection()
    {
        Vector3 pos = transform.root.position;
        TerrainType newTerrain = null;

        // Priority 1: Check sealed room via Room component
        // Room extends Zone, which tracks _charactersInside.
        // For now, we rely on MapController grid as the primary source.
        // Room.FloorTerrainType integration will come when CharacterLocations tracks current room.

        // Priority 2: Inside active MapController
        if (newTerrain == null)
        {
            var map = MapController.GetMapAtPosition(pos);
            if (map != null)
            {
                var grid = map.GetComponent<TerrainCellGrid>();
                if (grid != null)
                    newTerrain = grid.GetTerrainAt(pos);
            }
        }

        // Priority 3: Open world — Region default
        if (newTerrain == null)
        {
            var region = Region.GetRegionAtPosition(pos);
            if (region != null)
                newTerrain = region.GetDefaultTerrainType();
        }

        CurrentTerrainType = newTerrain;

        if (CurrentTerrainType != _lastTerrainType)
        {
            _lastTerrainType = CurrentTerrainType;
            OnTerrainChanged?.Invoke(CurrentTerrainType);
        }

        // Weather front detection
        var biome = Region.GetRegionAtPosition(pos);
        if (biome != null)
        {
            IsInWeatherFront = false;
            foreach (var front in biome.ActiveFronts)
            {
                if (front == null) continue;
                float dist = Vector3.Distance(pos, front.transform.position);
                if (dist < front.Radius.Value)
                {
                    IsInWeatherFront = true;
                    CurrentWeather = front.Type.Value;
                    break;
                }
            }
        }
    }

    private void ApplyTerrainEffects()
    {
        if (CurrentTerrainType == null || _character == null) return;

        // Speed modifier — will hook into CharacterMovement when API is exposed
        // For now the value is available via CurrentTerrainType.SpeedMultiplier

        // Damage over time
        if (CurrentTerrainType.HasDamage)
        {
            // TODO: Apply via Character health/combat system when damage API is available
            // float damage = CurrentTerrainType.DamagePerSecond * Time.deltaTime;
        }
    }
}
