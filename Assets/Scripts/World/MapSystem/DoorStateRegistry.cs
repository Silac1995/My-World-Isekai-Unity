using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side singleton that persists every door's lock and health state across
/// save/load. Keyed by <c>lockId</c> (which auto-derives from a building's BuildingId
/// for building doors, and is set explicitly by <see cref="BuildingInteriorSpawner"/>
/// for interior exit doors).
///
/// This is decoupled from <see cref="BuildingInteriorRegistry"/> on purpose — the
/// interior registry only holds records for buildings the player has actually entered,
/// so persisting door state through it would lose any unlock/lock done before first
/// entry. This registry tracks every lockId the moment its door is touched, regardless
/// of interior lifecycle.
///
/// Pair-sync (multiple doors sharing one lockId) and the static lockup registries on
/// <see cref="DoorLock"/> / <see cref="DoorHealth"/> are unrelated — they handle live
/// runtime propagation between paired doors. This class is just the save layer.
/// </summary>
public class DoorStateRegistry : MonoBehaviour, ISaveable
{
    public static DoorStateRegistry Instance { get; private set; }

    [Serializable]
    public class DoorStateRecord
    {
        public string LockId;
        public bool IsLocked = true;        // matches DoorLock._startsLocked default
        public float CurrentHealth = -1f;    // -1 = use prefab default (DoorHealth._maxHealth)
    }

    [Serializable]
    private class DoorStateSaveData
    {
        public List<DoorStateRecord> Records = new List<DoorStateRecord>();
    }

    private readonly Dictionary<string, DoorStateRecord> _records = new();

    public string SaveKey => "DoorStateRegistry_Data";

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
    /// Returns the existing record for <paramref name="lockId"/>, or creates a new
    /// one with default values and inserts it. Server-only.
    /// </summary>
    public DoorStateRecord GetOrCreate(string lockId)
    {
        if (string.IsNullOrEmpty(lockId)) return null;
        if (!_records.TryGetValue(lockId, out var record))
        {
            record = new DoorStateRecord { LockId = lockId };
            _records[lockId] = record;
        }
        return record;
    }

    /// <summary>
    /// Returns the existing record for <paramref name="lockId"/>, or null if none exists.
    /// </summary>
    public DoorStateRecord TryGet(string lockId)
    {
        if (string.IsNullOrEmpty(lockId)) return null;
        return _records.TryGetValue(lockId, out var record) ? record : null;
    }

    /// <summary>
    /// Server-only: records the current lock state under <paramref name="lockId"/>.
    /// Lazy-creates the record if it doesn't exist yet.
    /// </summary>
    public void SetLockState(string lockId, bool isLocked)
    {
        if (string.IsNullOrEmpty(lockId)) return;
        var record = GetOrCreate(lockId);
        record.IsLocked = isLocked;
    }

    /// <summary>
    /// Server-only: records the current health value under <paramref name="lockId"/>.
    /// Lazy-creates the record if it doesn't exist yet.
    /// </summary>
    public void SetHealthState(string lockId, float health)
    {
        if (string.IsNullOrEmpty(lockId)) return;
        var record = GetOrCreate(lockId);
        record.CurrentHealth = health;
    }

    #region ISaveable

    public object CaptureState()
    {
        var data = new DoorStateSaveData();
        data.Records.AddRange(_records.Values);
        return data;
    }

    public void RestoreState(object state)
    {
        if (state is not DoorStateSaveData data) return;

        _records.Clear();
        foreach (var record in data.Records)
        {
            if (record == null || string.IsNullOrEmpty(record.LockId)) continue;
            _records[record.LockId] = record;
        }

        Debug.Log($"<color=green>[DoorStateRegistry]</color> Restored {_records.Count} door state record(s).");

        // Apply restored state to any spawned doors. Some may have already spawned (scene-
        // authored buildings). DoorLock.ApplyLockState / DoorHealth.ApplyHealthState walk
        // the static component registries; we fall back to a scene-wide sweep so doors
        // that didn't register in time still get patched.
        ApplyAllToSpawnedDoors();
    }

    private void ApplyAllToSpawnedDoors()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        int locksApplied = 0, healthsApplied = 0;

        foreach (var doorLock in UnityEngine.Object.FindObjectsByType<DoorLock>(FindObjectsSortMode.None))
        {
            if (doorLock == null || !doorLock.IsSpawned) continue;
            string lockId = ResolveLockId(doorLock);
            if (string.IsNullOrEmpty(lockId)) continue;
            if (_records.TryGetValue(lockId, out var record))
            {
                doorLock.IsLocked.Value = record.IsLocked;
                locksApplied++;
            }
        }

        foreach (var doorHealth in UnityEngine.Object.FindObjectsByType<DoorHealth>(FindObjectsSortMode.None))
        {
            if (doorHealth == null || !doorHealth.IsSpawned) continue;
            string lockId = ResolveLockId(doorHealth);
            if (string.IsNullOrEmpty(lockId)) continue;
            if (_records.TryGetValue(lockId, out var record) && record.CurrentHealth >= 0f)
            {
                doorHealth.CurrentHealth.Value = record.CurrentHealth;
                doorHealth.IsBroken.Value = record.CurrentHealth <= 0f;
                healthsApplied++;
            }
        }

        Debug.Log($"<color=green>[DoorStateRegistry]</color> Applied state to {locksApplied} DoorLock(s), {healthsApplied} DoorHealth(s).");
    }

    private static string ResolveLockId(DoorLock doorLock)
    {
        string lockId = doorLock.LockId;
        if (!string.IsNullOrEmpty(lockId)) return lockId;
        var building = doorLock.GetComponentInParent<Building>();
        return building != null ? building.BuildingId : null;
    }

    private static string ResolveLockId(DoorHealth doorHealth)
    {
        string lockId = doorHealth.LockId;
        if (!string.IsNullOrEmpty(lockId)) return lockId;
        var building = doorHealth.GetComponentInParent<Building>();
        return building != null ? building.BuildingId : null;
    }

    #endregion
}
