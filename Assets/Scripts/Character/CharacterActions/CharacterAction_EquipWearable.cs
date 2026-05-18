using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: removes a wearable from a bag slot and equips it via
/// <see cref="CharacterEquipment.Equip"/>, which performs bag-first displacement
/// of any item currently in the target layer/slot (2026-05-19 rework).
///
/// <para>Queued server-side: either by the player-UI bridge ServerRpc
/// (<c>CharacterActions.RequestEquipmentVerbServerRpc</c>) or directly by future
/// NPC AI. Rule #22 player↔NPC parity. Coexists with the older
/// <see cref="CharacterEquipAction"/> (the latter takes a resolved
/// <see cref="EquipmentInstance"/> and is used by WorldItem wearable pickup
/// + GOAP equip-carried-clothing paths; this one takes a bag slot index so
/// the server can re-validate at apply-time against the live inventory).</para>
///
/// <para>Validation: the bag slot index must still hold a <see cref="WearableInstance"/>
/// at apply-time — slot contents may have shifted between queue and apply. Action
/// silently no-ops on mismatch (race is rare, self-correcting via next OnEquipmentChanged).</para>
/// </summary>
public sealed class CharacterAction_EquipWearable : CharacterAction
{
    private readonly int _bagSlotIndex;

    public int BagSlotIndex => _bagSlotIndex;

    public CharacterAction_EquipWearable(Character character, int bagSlotIndex)
        : base(character, 0f)
    {
        _bagSlotIndex = bagSlotIndex;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        var equip = character.CharacterEquipment;
        if (equip == null) return false;
        var inv = equip.GetInventory();
        if (inv == null || _bagSlotIndex < 0 || _bagSlotIndex >= inv.ItemSlots.Count) return false;
        var slot = inv.ItemSlots[_bagSlotIndex];
        return !slot.IsEmpty() && slot.ItemInstance is WearableInstance;
    }

    public override void OnStart() { /* no animation — UI-driven discrete action */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;
        var inv = equip.GetInventory();
        if (inv == null || _bagSlotIndex < 0 || _bagSlotIndex >= inv.ItemSlots.Count) return;

        var slot = inv.ItemSlots[_bagSlotIndex];
        if (slot.IsEmpty() || !(slot.ItemInstance is WearableInstance wearable))
        {
            if (NPCDebug.VerboseActions)
                Debug.LogWarning($"<color=orange>[EquipWearable]</color> {character.CharacterName} aborted: bag slot {_bagSlotIndex} no longer holds a wearable.");
            return;
        }

        // Remove from bag first, THEN call Equip. CharacterEquipment.Equip's bag-first
        // displacement needs the source slot free so the displaced wearable can land
        // in it if the bag is otherwise full.
        if (!inv.RemoveItem(wearable, character)) return;

        equip.Equip(wearable);
    }
}
