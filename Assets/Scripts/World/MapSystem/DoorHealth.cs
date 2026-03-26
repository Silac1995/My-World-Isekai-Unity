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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            CurrentHealth.Value = _maxHealth;
            IsBroken.Value = false;
        }
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
