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
            IsLocked.Value = _startsLocked;
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
