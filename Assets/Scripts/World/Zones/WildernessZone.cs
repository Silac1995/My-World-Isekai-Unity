using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Time;

namespace MWI.WorldSystem
{
    /// <summary>
    /// A virtual-content region inside a Region. Holds harvestables and future wildlife records
    /// that stream in only when a player is within spawn radius. Can move via pluggable
    /// IZoneMotionStrategy ScriptableObject assets evaluated daily by MacroSimulator.
    /// </summary>
    public class WildernessZone : NetworkBehaviour, IWorldZone, ISaveable
    {
        [Header("Identity")]
        [SerializeField] private string _zoneId;
        [SerializeField] private float _radius = 75f;

        [Header("Parent")]
        [SerializeField] private Region _parentRegion;  // null allowed — zone can exist outside any region

        [Header("Contents (data-layer)")]
        [SerializeField] private List<ResourcePoolEntry> _harvestables = new List<ResourcePoolEntry>();
        [SerializeField] private List<HibernatedNPCData> _wildlife = new List<HibernatedNPCData>();

        [Header("Motion")]
        [SerializeField] private List<ScriptableZoneMotionStrategy> _motionStrategies = new List<ScriptableZoneMotionStrategy>();

        [Header("Lifecycle")]
        [SerializeField] private bool _isDynamicallySpawned;

        /// <summary>True if this zone was spawned at runtime (not placed in the scene prefab).</summary>
        public bool IsDynamicallySpawned => _isDynamicallySpawned;
        public Region ParentRegion => _parentRegion;
        public List<ResourcePoolEntry> Harvestables => _harvestables;
        public List<HibernatedNPCData> Wildlife => _wildlife;
        public IReadOnlyList<ScriptableZoneMotionStrategy> MotionStrategies => _motionStrategies;

        // --- IWorldZone ---
        public string ZoneId => _zoneId;
        public Vector3 Center => transform.position;
        public float Radius => _radius;
        public bool Contains(Vector3 worldPos)
            => (worldPos - transform.position).sqrMagnitude <= _radius * _radius;
        public float DistanceTo(Vector3 worldPos)
        {
            float dist = Vector3.Distance(worldPos, transform.position);
            return Mathf.Max(0f, dist - _radius);
        }

        // --- Server-side init ---
        /// <summary>
        /// Called by WildernessZoneManager.SpawnZone after the NetworkObject is spawned.
        /// SERVER AUTHORITY ONLY. Phase 1 does not replicate _zoneId, _radius, _parentRegion,
        /// _harvestables, _wildlife, or _motionStrategies to clients — clients see the
        /// NetworkObject but all configuration stays default-valued until a future phase
        /// adds NetworkVariable wrappers. See ADR-0001.
        /// </summary>
        public void InitializeAsDynamic(string zoneId, float radius, Region parent,
            List<ScriptableZoneMotionStrategy> motionStrategies,
            List<ResourcePoolEntry> seededHarvestables)
        {
            if (!IsServer)
            {
                Debug.LogError("<color=red>[WildernessZone:InitializeAsDynamic]</color> Must be called on server.");
                return;
            }
            if (_isDynamicallySpawned)
            {
                Debug.LogWarning($"<color=yellow>[WildernessZone:InitializeAsDynamic]</color> Zone '{_zoneId}' already initialized — refusing to re-init. Authored zones should not be re-initialized as dynamic.");
                return;
            }
            _zoneId = zoneId;
            _radius = radius;
            _parentRegion = parent;
            _motionStrategies = motionStrategies != null
                ? new List<ScriptableZoneMotionStrategy>(motionStrategies)
                : new List<ScriptableZoneMotionStrategy>();
            _harvestables = seededHarvestables != null
                ? new List<ResourcePoolEntry>(seededHarvestables)
                : new List<ResourcePoolEntry>();
            _isDynamicallySpawned = true;
            parent?.RegisterWildernessZone(this);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _parentRegion?.UnregisterWildernessZone(this);
        }

        // --- Save ---
        public string SaveKey => string.IsNullOrEmpty(_zoneId) ? null : $"WildernessZone_{_zoneId}";

        /// <summary>
        /// Captures the zone's current state for serialization by its parent Region.
        /// This is called by Region.CaptureState for dynamic zones only (authored zones restore from scene).
        /// </summary>
        public WildernessZoneSaveData CaptureZoneState()
        {
            var data = new WildernessZoneSaveData
            {
                ZoneId = _zoneId,
                Center = transform.position,
                Radius = _radius,
                IsDynamicallySpawned = _isDynamicallySpawned,
                Harvestables = new List<ResourcePoolEntry>(_harvestables),
                Wildlife = new List<HibernatedNPCData>(_wildlife),
            };

            foreach (var strategy in _motionStrategies)
            {
                if (strategy == null) continue;
                // Resources path (strip "Assets/Resources/" prefix and ".asset" suffix at callsite if needed).
                // For simplicity, store the asset name; WildernessZoneManager resolves via Resources.Load.
                data.MotionStrategyAssetPaths.Add(strategy.name);
            }

            return data;
        }

        // Boilerplate ISaveable (individual zone saves go through Region aggregation, so these are no-ops).
        public object CaptureState() => null;
        public void RestoreState(object state) { }
    }
}
