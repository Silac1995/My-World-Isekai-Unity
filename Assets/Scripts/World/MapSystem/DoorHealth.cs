using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked health component for breakable doors.
/// Add to the same GameObject as MapTransitionDoor and DoorLock.
/// </summary>
public class DoorHealth : NetworkBehaviour, IDamageable
{
    [Header("Health Settings")]
    [SerializeField] private bool _isBreakable = true;
    [SerializeField] private float _maxHealth = 100f;
    [Tooltip("Percentage of incoming damage absorbed (0.0 = no resistance, 1.0 = invulnerable)")]
    [SerializeField, Range(0f, 1f)] private float _damageResistance = 0f;

    [Header("Repair")]
    [SerializeField] private List<CraftingIngredient> _repairMaterials = new List<CraftingIngredient>();

    public NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsBroken = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsBreakable => _isBreakable;
    public float MaxHealth => _maxHealth;
    public float DamageResistance => _damageResistance;
    public List<CraftingIngredient> RepairMaterials => _repairMaterials;

    // Cached at spawn — auto-derived from parent Building's BuildingId, mirrors DoorLock's
    // lockId scheme so the same key reaches the same BuildingInteriorRegistry record.
    private string _lockId;
    public string LockId => _lockId;

    // Static registry keyed by lockId, used by BuildingInteriorRegistry to apply persisted
    // health on restore without scanning the whole scene.
    private static readonly Dictionary<string, List<DoorHealth>> _registry = new();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Auto-derive lockId from parent Building (same scheme as DoorLock).
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
            // Prefer persisted health from DoorStateRegistry, else _maxHealth default.
            float initialHealth = _maxHealth;
            if (!string.IsNullOrEmpty(_lockId) && DoorStateRegistry.Instance != null)
            {
                var record = DoorStateRegistry.Instance.TryGet(_lockId);
                if (record != null && record.CurrentHealth >= 0f)
                    initialHealth = record.CurrentHealth;
            }
            CurrentHealth.Value = initialHealth;
            IsBroken.Value = initialHealth <= 0f;
        }

        CurrentHealth.OnValueChanged += OnCurrentHealthChanged;

        if (!string.IsNullOrEmpty(_lockId))
        {
            if (!_registry.ContainsKey(_lockId))
                _registry[_lockId] = new List<DoorHealth>();
            _registry[_lockId].Add(this);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        CurrentHealth.OnValueChanged -= OnCurrentHealthChanged;

        if (!string.IsNullOrEmpty(_lockId) && _registry.TryGetValue(_lockId, out var list))
        {
            list.Remove(this);
            if (list.Count == 0)
                _registry.Remove(_lockId);
        }
    }

    private void OnCurrentHealthChanged(float previousValue, float newValue)
    {
        if (!IsServer) return;
        PersistHealthState(newValue);
    }

    private void PersistHealthState(float health)
    {
        // Defensive: re-derive lockId if it never got set at OnNetworkSpawn.
        if (string.IsNullOrEmpty(_lockId))
        {
            var building = GetComponentInParent<Building>();
            if (building != null && !string.IsNullOrEmpty(building.BuildingId))
            {
                _lockId = building.BuildingId;
                if (!_registry.ContainsKey(_lockId))
                    _registry[_lockId] = new List<DoorHealth>();
                if (!_registry[_lockId].Contains(this))
                    _registry[_lockId].Add(this);
            }
        }

        if (string.IsNullOrEmpty(_lockId)) return;
        DoorStateRegistry.Instance?.SetHealthState(_lockId, health);
    }

    /// <summary>
    /// Server-only: applies a health value to every spawned DoorHealth with the given lockId.
    /// Called by BuildingInteriorRegistry.RestoreState to retroactively fix doors that
    /// spawned from the scene before the save was restored.
    /// </summary>
    public static void ApplyHealthState(string lockId, float health)
    {
        if (string.IsNullOrEmpty(lockId)) return;
        if (!_registry.TryGetValue(lockId, out var doors)) return;
        foreach (var door in doors)
        {
            if (door != null && door.IsServer && door.IsSpawned)
            {
                door.CurrentHealth.Value = health;
                door.IsBroken.Value = health <= 0f;
            }
        }
    }

    /// <summary>
    /// Returns the current CurrentHealth of any spawned DoorHealth with the given lockId,
    /// or null if none can be found. Falls back to a scene-wide scan when the static
    /// `_registry` doesn't have the lockId (defensive — covers empty-lockId-at-spawn cases).
    /// </summary>
    public static float? GetCurrentHealth(string lockId)
    {
        if (string.IsNullOrEmpty(lockId)) return null;

        if (_registry.TryGetValue(lockId, out var doors))
        {
            foreach (var door in doors)
            {
                if (door != null && door.IsSpawned) return door.CurrentHealth.Value;
            }
        }

        foreach (var door in UnityEngine.Object.FindObjectsByType<DoorHealth>(FindObjectsSortMode.None))
        {
            if (door == null || !door.IsSpawned) continue;
            string resolvedLockId = door._lockId;
            if (string.IsNullOrEmpty(resolvedLockId))
            {
                var building = door.GetComponentInParent<Building>();
                if (building != null) resolvedLockId = building.BuildingId;
            }
            if (resolvedLockId == lockId) return door.CurrentHealth.Value;
        }

        return null;
    }

    public bool CanBeDamaged()
    {
        return _isBreakable && !IsBroken.Value;
    }

    public void TakeDamage(float damage, Character attacker)
    {
        if (!IsServer) return;
        if (!CanBeDamaged()) return;

        float effectiveDamage = damage * (1f - _damageResistance);
        CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - effectiveDamage);

        Debug.Log($"<color=red>[DoorHealth]</color> '{name}' took {effectiveDamage:F1} damage (raw={damage:F1}, resist={_damageResistance:P0}). HP: {CurrentHealth.Value:F1}/{_maxHealth}");

        if (CurrentHealth.Value <= 0f)
        {
            IsBroken.Value = true;
            Debug.Log($"<color=red>[DoorHealth]</color> '{name}' is now BROKEN.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestRepairServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsBroken.Value) return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        NetworkObject playerObj = NetworkManager.ConnectedClients[clientId].PlayerObject;
        if (playerObj == null) return;

        Character repairer = playerObj.GetComponent<Character>();
        if (repairer == null) return;

        // Validate and consume materials
        var inventory = repairer.CharacterEquipment.GetInventory();
        if (inventory == null) return;

        // Check all materials are available
        foreach (var ingredient in _repairMaterials)
        {
            if (ingredient.Item == null) continue;
            if (!repairer.CharacterEquipment.HasItemSO(ingredient.Item))
            {
                Debug.Log($"<color=orange>[DoorHealth]</color> Repair failed: {repairer.CharacterName} missing {ingredient.Item.ItemName}");
                return;
            }
        }

        // Consume materials
        foreach (var ingredient in _repairMaterials)
        {
            if (ingredient.Item == null) continue;
            for (int i = 0; i < ingredient.Amount; i++)
            {
                // Find and remove one instance of this item
                foreach (var slot in inventory.ItemSlots)
                {
                    if (!slot.IsEmpty() && slot.ItemInstance.ItemSO == ingredient.Item)
                    {
                        inventory.RemoveItem(slot.ItemInstance, repairer);
                        break;
                    }
                }
            }
        }

        // Restore door
        CurrentHealth.Value = _maxHealth;
        IsBroken.Value = false;

        Debug.Log($"<color=green>[DoorHealth]</color> '{name}' repaired by {repairer.CharacterName}. HP restored to {_maxHealth}.");
    }
}
