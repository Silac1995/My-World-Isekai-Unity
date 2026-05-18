using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: moves an item (from bag / worn / active weapon) into the
/// character's hands. Implements the smart-swap rule from the 2026-05-19 design
/// spec §6 — if hands are already occupied with Y, Y is stashed back to the bag
/// first (covers the "slot was free" AND the "same-type fits in X's now-empty
/// source slot" cases naturally); only drops Y to the ground when the bag
/// genuinely has no compatible space.
///
/// <para>Sources supported: BagSlot, WornSlot, ActiveWeapon. HandsCarry is invalid
/// (item is already in the hand).</para>
///
/// <para>Active-Weapon source: wield-off via <see cref="CharacterEquipment.WieldOffToHand"/>
/// (does NOT drop the weapon to world); then carry the detached weapon in hand
/// after applying the smart-swap to whatever was already carried.</para>
///
/// <para>Queued server-side: either by the player-UI bridge ServerRpc
/// (<c>CharacterActions.RequestEquipmentVerbServerRpc</c>) or directly by future
/// NPC AI. Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_CarryInHand : CharacterAction
{
    private readonly EquipmentSourceRef _source;

    public EquipmentSourceRef Source => _source;

    public CharacterAction_CarryInHand(Character character, EquipmentSourceRef source)
        : base(character, 0f)
    {
        _source = source;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (_source.Kind == EquipmentSourceKind.HandsCarry) return false; // already in hand
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (equip == null || hands == null) return;

        // 1. Detach X from its source (this may free a bag/worn slot).
        ItemInstance x = DetachFromSource(equip);
        if (x == null) return;

        ItemInstance y = hands.CarriedItem;

        // 2a. Easy case — hand was free.
        if (y == null)
        {
            hands.CarryItem(x);
            return;
        }

        // 2b. Hand was occupied. Try to stash Y first.
        // HasFreeSpaceForItem now considers X's freshly-vacated slot if X was bag-sourced.
        var inv = equip.GetInventory();
        if (inv != null && inv.HasFreeSpaceForItem(y))
        {
            hands.DropCarriedItem();            // clears Y from hand without spawning WorldItem
            inv.AddItem(y, character);          // Y → bag
            hands.CarryItem(x);                 // X → hand
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[CarryInHand]</color> {character.CharacterName} swapped via bag: hand {y.ItemSO.ItemName} → bag, {x.ItemSO.ItemName} → hand.");
        }
        else
        {
            // 2c. No bag space for Y — Y goes to ground, X to hand.
            hands.DropCarriedItem();
            CharacterDropItem.ExecutePhysicalDrop(character, y);
            hands.CarryItem(x);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[CarryInHand]</color> {character.CharacterName} swapped via ground: hand {y.ItemSO.ItemName} → ground, {x.ItemSO.ItemName} → hand.");
        }
    }

    /// <summary>
    /// Removes X from its source location and returns it. Bag/worn slots become empty;
    /// active weapon is detached via WieldOffToHand (does NOT drop to world).
    /// Returns null if the source no longer holds a valid item (race).
    /// </summary>
    private ItemInstance DetachFromSource(CharacterEquipment equip)
    {
        switch (_source.Kind)
        {
            case EquipmentSourceKind.BagSlot:
            {
                var inv = equip.GetInventory();
                if (inv == null || _source.BagIndex < 0 || _source.BagIndex >= inv.ItemSlots.Count) return null;
                var slot = inv.ItemSlots[_source.BagIndex];
                if (slot.IsEmpty()) return null;
                ItemInstance item = slot.ItemInstance;
                inv.RemoveItem(item, character);
                return item;
            }
            case EquipmentSourceKind.WornSlot:
                return equip.DetachWornToCaller(_source.Layer, _source.Slot);
            case EquipmentSourceKind.ActiveWeapon:
                return equip.WieldOffToHand();
            default:
                return null;
        }
    }
}
