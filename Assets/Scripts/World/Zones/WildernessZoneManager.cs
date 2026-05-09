using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Time;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Server-side singleton that spawns and restores WildernessZones at runtime.
    /// Enforces WorldSettingsData.MapMinSeparation across all IWorldZone centers.
    /// </summary>
    public class WildernessZoneManager : MonoBehaviour
    {
        public static WildernessZoneManager Instance { get; private set; }

        [SerializeField] private WorldSettingsData _settings;
        [SerializeField] private GameObject _wildernessZonePrefab;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (_settings == null)
            {
                _settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            }
            if (_wildernessZonePrefab == null)
            {
                _wildernessZonePrefab = Resources.Load<GameObject>("Prefabs/World/WildernessZone");
            }
        }

        /// <summary>
        /// Server-only. Spawns a new WildernessZone at the given position using the provided def.
        /// If parent is null, the parent Region is auto-resolved via Region.GetRegionAtPosition(pos).
        /// Rejects the spawn if another IWorldZone center is within MapMinSeparation.
        /// </summary>
        /// <returns>The new WildernessZone, or null on failure.</returns>
        public WildernessZone SpawnZone(Vector3 pos, WildernessZoneDef def, Region parent = null)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> Must run on the server.");
                return null;
            }
            if (def == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> WildernessZoneDef is null.");
                return null;
            }
            if (_wildernessZonePrefab == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> WildernessZone prefab not assigned or found at Resources/Prefabs/World/WildernessZone.");
                return null;
            }

            float minSep = _settings != null ? _settings.MapMinSeparation : 150f;
            if (IsTooCloseToExistingZone(pos, minSep, out string conflictId))
            {
                Debug.LogWarning($"<color=yellow>[WildernessZoneManager:SpawnZone]</color> Rejected spawn at {pos}: within {minSep} units of existing zone '{conflictId}'.");
                return null;
            }

            parent = parent ?? Region.GetRegionAtPosition(pos);

            GameObject obj;
            try
            {
                obj = Instantiate(_wildernessZonePrefab, pos, Quaternion.identity);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }

            var zone = obj.GetComponent<WildernessZone>();
            if (zone == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> Prefab missing WildernessZone component.");
                Destroy(obj);
                return null;
            }

            var netObj = obj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> Prefab missing NetworkObject component.");
                Destroy(obj);
                return null;
            }

            try
            {
                netObj.Spawn();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Destroy(obj);
                return null;
            }

            string zoneId = $"Wild_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;
            var initialPool = def.HarvestableSeedTable != null
                ? def.HarvestableSeedTable.BuildInitialPool(currentDay)
                : new List<ResourcePoolEntry>();

            zone.InitializeAsDynamic(zoneId, def.DefaultRadius, parent, def.DefaultMotion, initialPool);

            // Reparent under the Region's NetworkObject via NGO-aware TrySetParent.
            // Region is a NetworkBehaviour with its own NetworkObject, so this replicates
            // cleanly to clients.
            if (parent != null)
            {
                bool parented = netObj.TrySetParent(parent.transform, worldPositionStays: true);
                if (!parented)
                {
                    Debug.LogWarning($"<color=yellow>[WildernessZoneManager:SpawnZone]</color> NGO TrySetParent failed for zone '{zoneId}' under region '{parent.ZoneId}'. Zone remains at scene root but is still logically registered.");
                }
            }

            Debug.Log($"<color=magenta>[WildernessZoneManager:SpawnZone]</color> Spawned '{zoneId}' at {pos} (parent={parent?.ZoneId ?? "<none>"}, radius={def.DefaultRadius}).");
            return zone;
        }

        /// <summary>
        /// Server-only. Restores a WildernessZone from save data under the given parent Region.
        /// Called by Region.RestoreState during save-load.
        /// </summary>
        public WildernessZone RestoreZone(WildernessZoneSaveData data, Region parent)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return null;
            if (data == null || _wildernessZonePrefab == null) return null;

            GameObject obj = Instantiate(_wildernessZonePrefab, data.Center, Quaternion.identity);
            var zone = obj.GetComponent<WildernessZone>();
            var netObj = obj.GetComponent<NetworkObject>();
            if (zone == null || netObj == null)
            {
                Destroy(obj);
                return null;
            }

            netObj.Spawn();

            // Re-resolve motion strategies from Resources paths (stored as asset names).
            // If two SO assets share a name under Resources/, the first-found wins and a
            // warning is logged. See code-review follow-up from Tasks 7+8.
            var motion = new List<ScriptableZoneMotionStrategy>();
            foreach (string name in data.MotionStrategyAssetPaths)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var strat = Resources.Load<ScriptableZoneMotionStrategy>($"Data/World/Motion/{name}");
                if (strat != null)
                {
                    motion.Add(strat);
                }
                else
                {
                    Debug.LogWarning($"<color=yellow>[WildernessZoneManager:RestoreZone]</color> Motion strategy '{name}' not found under Resources/Data/World/Motion/. Zone '{data.ZoneId}' will drift-less for this strategy slot.");
                }
            }

            zone.InitializeAsDynamic(data.ZoneId, data.Radius, parent, motion, data.Harvestables);

            // Re-apply wildlife records (empty in Phase 1)
            zone.Wildlife.Clear();
            zone.Wildlife.AddRange(data.Wildlife);

            Debug.Log($"<color=green>[WildernessZoneManager:RestoreZone]</color> Restored '{data.ZoneId}' under region '{parent?.ZoneId ?? "<none>"}'.");
            return zone;
        }

        // --- Internal ---

        private bool IsTooCloseToExistingZone(Vector3 pos, float minSep, out string conflictId)
        {
            conflictId = null;
            float sqr = minSep * minSep;

            var allMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in allMaps)
            {
                if (m == null || m.Type == MapType.Interior) continue;
                if ((m.transform.position - pos).sqrMagnitude < sqr)
                {
                    conflictId = m.MapId;
                    return true;
                }
            }

            var allZones = UnityEngine.Object.FindObjectsByType<WildernessZone>(FindObjectsSortMode.None);
            foreach (var z in allZones)
            {
                if (z == null) continue;
                if ((z.transform.position - pos).sqrMagnitude < sqr)
                {
                    conflictId = z.ZoneId;
                    return true;
                }
            }

            return false;
        }
    }
}
