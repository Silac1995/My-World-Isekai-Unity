using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Server-side singleton that maps building instances to their interior MapControllers.
/// Persists via ISaveable so interior allocations survive server restarts.
/// </summary>
public class BuildingInteriorRegistry : MonoBehaviour, ISaveable
{
    public static BuildingInteriorRegistry Instance { get; private set; }

    [Serializable]
    public class InteriorRecord
    {
        public string BuildingId;
        public string InteriorMapId;
        public int SlotIndex;
        public string ExteriorMapId;
        public Vector3 ExteriorDoorPosition;
        public string PrefabId;

        // Door state persistence (survives hibernation)
        public bool IsLocked = true;
        public float DoorCurrentHealth = -1f; // -1 means use prefab default
    }

    [Serializable]
    private class InteriorRegistrySaveData
    {
        public List<InteriorRecord> Records = new List<InteriorRecord>();
    }

    private Dictionary<string, InteriorRecord> _interiors = new Dictionary<string, InteriorRecord>();

    public string SaveKey => "BuildingInteriorRegistry_Data";

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
        Invoke(nameof(RegisterWithSaveManager), 0.5f);
    }

    private void RegisterWithSaveManager()
    {
        if (SaveManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            SaveManager.Instance.RegisterWorldSaveable(this);
        }
    }

    private void OnDestroy()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.UnregisterWorldSaveable(this);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Finds an InteriorRecord by matching the exterior door position.
    /// Used by DoorLock for paired interior door sound propagation.
    /// </summary>
    public InteriorRecord FindRecordByDoorPosition(Vector3 doorPosition, float tolerance = 1f)
    {
        foreach (var record in _interiors.Values)
        {
            if (Vector3.Distance(record.ExteriorDoorPosition, doorPosition) <= tolerance)
                return record;
        }
        return null;
    }

    /// <summary>
    /// Checks if an interior already exists for the given building instance.
    /// </summary>
    public bool TryGetInterior(string buildingId, out InteriorRecord record)
    {
        return _interiors.TryGetValue(buildingId, out record);
    }

    /// <summary>
    /// Registers a new interior for a building. Allocates a spatial slot via WorldOffsetAllocator.
    /// Server-only.
    /// </summary>
    public InteriorRecord RegisterInterior(string buildingId, string prefabId, string exteriorMapId, Vector3 exteriorDoorPosition)
    {
        if (_interiors.ContainsKey(buildingId))
        {
            Debug.LogWarning($"[BuildingInteriorRegistry] Interior already registered for building '{buildingId}'. Returning existing.");
            return _interiors[buildingId];
        }

        int slotIndex = WorldOffsetAllocator.Instance.AllocateSlotIndex();
        if (slotIndex < 0)
        {
            Debug.LogError("[BuildingInteriorRegistry] Failed to allocate slot for interior.");
            return null;
        }

        // Snapshot the live exterior door state so changes made BEFORE first entry are
        // not lost when the record is created (the live door is the sole source of truth
        // until this record exists). Falls back to field defaults if no door is spawned
        // for this lockId yet.
        bool? liveLockState = DoorLock.GetCurrentLockState(buildingId);
        float? liveHealth = DoorHealth.GetCurrentHealth(buildingId);

        var record = new InteriorRecord
        {
            BuildingId = buildingId,
            InteriorMapId = $"{exteriorMapId}_Interior_{buildingId}",
            SlotIndex = slotIndex,
            ExteriorMapId = exteriorMapId,
            ExteriorDoorPosition = exteriorDoorPosition,
            PrefabId = prefabId,
            IsLocked = liveLockState ?? true,
            DoorCurrentHealth = liveHealth ?? -1f
        };

        _interiors[buildingId] = record;
        Debug.Log($"<color=green>[BuildingInteriorRegistry]</color> Registered interior '{record.InteriorMapId}' at slot {slotIndex} for building '{buildingId}'.");
        return record;
    }

    /// <summary>
    /// Removes an interior record and releases its spatial slot.
    /// </summary>
    public void UnregisterInterior(string buildingId)
    {
        if (_interiors.TryGetValue(buildingId, out var record))
        {
            WorldOffsetAllocator.Instance.ReleaseSlot(record.SlotIndex);
            _interiors.Remove(buildingId);
            Debug.Log($"[BuildingInteriorRegistry] Unregistered interior for building '{buildingId}', released slot {record.SlotIndex}.");
        }
    }

    #region ISaveable

    public object CaptureState()
    {
        var data = new InteriorRegistrySaveData();
        data.Records.AddRange(_interiors.Values);
        return data;
    }

    public void RestoreState(object state)
    {
        if (state is InteriorRegistrySaveData data)
        {
            _interiors.Clear();

            foreach (var record in data.Records)
            {
                _interiors[record.BuildingId] = record;
            }

            Debug.Log($"<color=green>[BuildingInteriorRegistry]</color> Restored {_interiors.Count} interior records.");

            // Door lock/health state is handled by DoorStateRegistry (a separate ISaveable),
            // not this registry. The legacy IsLocked / DoorCurrentHealth fields on
            // InteriorRecord are unused now and remain only for save-file backward compat.

            // Respawn interior MapControllers at their allocated offsets
            RespawnInteriors();
        }
    }

    private void RespawnInteriors()
    {
        WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
        if (settings == null)
        {
            Debug.LogError("[BuildingInteriorRegistry] WorldSettingsData not found. Cannot respawn interiors.");
            return;
        }

        foreach (var record in _interiors.Values)
        {
            GameObject interiorPrefab = settings.GetInteriorPrefab(record.PrefabId);
            if (interiorPrefab == null)
            {
                Debug.LogWarning($"[BuildingInteriorRegistry] No InteriorPrefab found for PrefabId '{record.PrefabId}'. Skipping respawn for '{record.InteriorMapId}'.");
                continue;
            }

            BuildingInteriorSpawner.SpawnInterior(record, interiorPrefab);
        }
    }

    #endregion
}
