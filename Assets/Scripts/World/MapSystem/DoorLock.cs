using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked lock component for doors. Add to the same GameObject as MapTransitionDoor.
/// Requires a NetworkObject in the parent hierarchy.
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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        IsLocked.OnValueChanged -= OnIsLockedChanged;
    }

    /// <summary>
    /// Returns true if the door can be passed through (unlocked OR broken).
    /// </summary>
    public bool CanPass()
    {
        // Broken doors are always passable
        var doorHealth = GetComponent<DoorHealth>();
        if (doorHealth != null && doorHealth.IsBroken.Value)
            return true;

        return !IsLocked.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUnlockServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsLocked.Value) return;
        IsLocked.Value = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestLockServerRpc(ServerRpcParams rpcParams = default)
    {
        if (IsLocked.Value) return;

        // Can't lock a broken door
        var doorHealth = GetComponent<DoorHealth>();
        if (doorHealth != null && doorHealth.IsBroken.Value) return;

        IsLocked.Value = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestJiggleServerRpc(ServerRpcParams rpcParams = default)
    {
        PlayJiggleClientRpc();

        // Propagate to paired interior door via registry
        PropagateJiggleToPairedDoor();
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
    /// Server-only: find the paired door via BuildingInteriorRegistry and play jiggle there too.
    /// </summary>
    private void PropagateJiggleToPairedDoor()
    {
        if (!IsServer) return;
        if (BuildingInteriorRegistry.Instance == null) return;

        // Find the record for this door's building
        var record = BuildingInteriorRegistry.Instance.FindRecordByDoorPosition(transform.position);
        if (record == null) return;

        // Find the interior MapController and its DoorLock
        var interiorMap = MapController.GetByMapId(record.InteriorMapId);
        if (interiorMap == null) return;

        var pairedLock = interiorMap.GetComponentInChildren<DoorLock>();
        if (pairedLock != null && pairedLock != this)
        {
            pairedLock.PlayJiggleClientRpc();
        }
    }
}
