using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked lock component for doors. Add to the same GameObject as MapTransitionDoor.
/// Requires a NetworkObject in the parent hierarchy.
/// Doors with the same LockId are automatically paired — lock/unlock and jiggle
/// propagate to all paired doors.
/// </summary>
public class DoorLock : NetworkBehaviour
{
    [Header("Lock Settings")]
    [SerializeField] private string _lockId;
    [SerializeField] private int _requiredTier = 0;
    [SerializeField] private bool _startsLocked = true;

    [Header("Audio")]
    [SerializeField] private AudioClip _jiggleSFX;
    [SerializeField] private AudioClip _unlockSFX;
    [SerializeField] private AudioClip _lockSFX;

    public NetworkVariable<bool> IsLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public string LockId => _lockId;
    public int RequiredTier => _requiredTier;

    private AudioSource _audioSource;

    // --- Static registry: all spawned DoorLocks grouped by LockId ---
    private static readonly Dictionary<string, List<DoorLock>> _registry = new();

    /// <summary>
    /// Sets the LockId at runtime. Call BEFORE NetworkObject.Spawn() so
    /// OnNetworkSpawn registers with the correct key.
    /// Used by BuildingInteriorSpawner to assign building-unique lock IDs.
    /// </summary>
    public void SetLockId(string lockId) => _lockId = lockId;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Auto-derive LockId from parent Building if not explicitly set.
        // Each building instance has a unique BuildingId (GUID), so each
        // building's doors get a unique lock — same prefab, different locks.
        if (string.IsNullOrEmpty(_lockId))
        {
            var building = GetComponentInParent<Building>();
            if (building != null && !string.IsNullOrEmpty(building.BuildingId))
            {
                _lockId = building.BuildingId;
            }
        }

        if (IsServer)
        {
            // Prefer persisted state from DoorStateRegistry (lazy-created the moment
            // the door is interacted with, regardless of whether the interior has been
            // visited). Falls back to the authored `_startsLocked` default if no record
            // exists yet — applies to both exterior building doors AND interior
            // `MapTransitionDoor`s symmetrically.
            bool initial = _startsLocked;
            if (!string.IsNullOrEmpty(_lockId) && DoorStateRegistry.Instance != null)
            {
                var record = DoorStateRegistry.Instance.TryGet(_lockId);
                if (record != null) initial = record.IsLocked;
            }
            IsLocked.Value = initial;
        }

        IsLocked.OnValueChanged += OnIsLockedChanged;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 1f; // 3D sound
            _audioSource.playOnAwake = false;
        }

        // Register in static LockId registry
        if (!string.IsNullOrEmpty(_lockId))
        {
            if (!_registry.ContainsKey(_lockId))
                _registry[_lockId] = new List<DoorLock>();
            _registry[_lockId].Add(this);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        IsLocked.OnValueChanged -= OnIsLockedChanged;

        // Unregister from static LockId registry
        if (!string.IsNullOrEmpty(_lockId) && _registry.TryGetValue(_lockId, out var list))
        {
            list.Remove(this);
            if (list.Count == 0)
                _registry.Remove(_lockId);
        }
    }

    [ContextMenu("Unlock")]
    private void DebugUnlock() => SetLockedStateWithSync(false);

    [ContextMenu("Lock")]
    private void DebugLock() => SetLockedStateWithSync(true);

    /// <summary>
    /// Returns true if the door can be passed through (unlocked OR broken).
    /// </summary>
    public bool CanPass()
    {
        var doorHealth = GetComponent<DoorHealth>();
        if (doorHealth != null && doorHealth.IsBroken.Value)
            return true;

        return !IsLocked.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUnlockServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsLocked.Value) return;
        SetLockedStateWithSync(false);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestLockServerRpc(ServerRpcParams rpcParams = default)
    {
        if (IsLocked.Value) return;

        var doorHealth = GetComponent<DoorHealth>();
        if (doorHealth != null && doorHealth.IsBroken.Value) return;

        SetLockedStateWithSync(true);
    }

    /// <summary>
    /// Server-only: sets lock state on this door AND all paired doors with the same LockId.
    /// Also persists the new state into <see cref="BuildingInteriorRegistry"/> so it survives save/load.
    /// </summary>
    private void SetLockedStateWithSync(bool locked)
    {
        if (!IsServer) return;

        IsLocked.Value = locked;

        // Sync to all paired doors
        foreach (var paired in GetPairedDoors())
        {
            paired.IsLocked.Value = locked;
        }

        PersistLockState(locked);
    }

    /// <summary>
    /// Writes the new lock state into <see cref="DoorStateRegistry"/> so it round-trips
    /// through save/load. The registry lazy-creates a record on first write, so the
    /// state survives even if the player unlocks/locks before the building's interior
    /// has ever been entered.
    /// </summary>
    private void PersistLockState(bool locked)
    {
        // Defensive: if _lockId wasn't auto-derived at OnNetworkSpawn (e.g. parent Building
        // hadn't set its BuildingId yet), retry now.
        if (string.IsNullOrEmpty(_lockId))
        {
            var building = GetComponentInParent<Building>();
            if (building != null && !string.IsNullOrEmpty(building.BuildingId))
            {
                _lockId = building.BuildingId;
                if (!_registry.ContainsKey(_lockId))
                    _registry[_lockId] = new List<DoorLock>();
                if (!_registry[_lockId].Contains(this))
                    _registry[_lockId].Add(this);
            }
        }

        if (string.IsNullOrEmpty(_lockId)) return;
        DoorStateRegistry.Instance?.SetLockState(_lockId, locked);
    }

    /// <summary>
    /// Server-only: applies a lock state to every spawned DoorLock with the given lockId.
    /// Used by post-restore sweeps to retroactively patch doors whose `OnNetworkSpawn`
    /// already ran with stale or empty registry state.
    /// </summary>
    public static void ApplyLockState(string lockId, bool isLocked)
    {
        if (string.IsNullOrEmpty(lockId)) return;
        if (!_registry.TryGetValue(lockId, out var doors)) return;
        foreach (var door in doors)
        {
            if (door == null || !door.IsServer || !door.IsSpawned) continue;
            door.IsLocked.Value = isLocked;
        }
    }

    /// <summary>
    /// Returns the current IsLocked value of any spawned DoorLock with the given lockId,
    /// or null if no such door can be found. Used by BuildingInteriorRegistry when first
    /// creating a record so it inherits the live exterior door's state instead of
    /// resetting to the field default. Falls back to a scene-wide scan when the static
    /// `_registry` doesn't have the lockId (defensive — covers empty-lockId-at-spawn cases).
    /// </summary>
    public static bool? GetCurrentLockState(string lockId)
    {
        if (string.IsNullOrEmpty(lockId)) return null;

        // Fast path: static registry.
        if (_registry.TryGetValue(lockId, out var doors))
        {
            foreach (var door in doors)
            {
                if (door != null && door.IsSpawned) return door.IsLocked.Value;
            }
        }

        // Fallback: scene scan. Resolves doors whose `_lockId` field never got set.
        foreach (var door in UnityEngine.Object.FindObjectsByType<DoorLock>(FindObjectsSortMode.None))
        {
            if (door == null || !door.IsSpawned) continue;
            string resolvedLockId = door._lockId;
            if (string.IsNullOrEmpty(resolvedLockId))
            {
                var building = door.GetComponentInParent<Building>();
                if (building != null) resolvedLockId = building.BuildingId;
            }
            if (resolvedLockId == lockId) return door.IsLocked.Value;
        }

        return null;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestJiggleServerRpc(ServerRpcParams rpcParams = default)
    {
        // Play on this door
        PlayJiggleClientRpc();

        // Play on all paired doors
        foreach (var paired in GetPairedDoors())
        {
            paired.PlayJiggleClientRpc();
        }
    }

    [ClientRpc]
    private void PlayJiggleClientRpc()
    {
        if (_audioSource != null && _jiggleSFX != null)
        {
            _audioSource.PlayOneShot(_jiggleSFX);
        }
    }

    private void OnIsLockedChanged(bool previousValue, bool newValue)
    {
        if (_audioSource == null) return;

        if (newValue && _lockSFX != null)
            _audioSource.PlayOneShot(_lockSFX);
        else if (!newValue && _unlockSFX != null)
            _audioSource.PlayOneShot(_unlockSFX);
    }

    /// <summary>
    /// Returns all other DoorLock instances that share this door's LockId.
    /// </summary>
    private List<DoorLock> GetPairedDoors()
    {
        var result = new List<DoorLock>();
        if (string.IsNullOrEmpty(_lockId)) return result;
        if (!_registry.TryGetValue(_lockId, out var list)) return result;

        foreach (var door in list)
        {
            if (door != null && door != this && door.IsSpawned)
                result.Add(door);
        }
        return result;
    }
}
