using System;
using UnityEngine;

/// <summary>
/// Transfers an <see cref="ItemInstance"/> from the character's inventory or hands
/// straight into a <see cref="StorageFurniture"/> slot. No <see cref="WorldItem"/>
/// is spawned — the item lives logical-only inside the furniture's <c>ItemSlots</c>.
///
/// Used by the logistics cycle: <c>GoapAction_GatherStorageItems</c> /
/// <c>GoapAction_DepositResources</c> walk the worker to the furniture's interaction
/// point and queue this action instead of <see cref="CharacterDropItem"/>.
///
/// <para>Server-authoritative on the chest side. <see cref="StorageFurniture"/>
/// contents replicate via <see cref="StorageFurnitureNetworkSync"/>'s NetworkList,
/// so any slot mutation in <c>OnApplyEffect</c> propagates to every peer.</para>
///
/// <para><b>Source-resolution modes</b> — the action knows three ways to find the
/// outgoing item, selected via the constructor's <c>sourceSlotIndex</c> parameter:</para>
/// <list type="bullet">
/// <item><description><b>-2 (legacy / by-reference, NPC GOAP path)</b>: server-side
///   bag/hands is searched for an <see cref="ItemInstance"/> that <c>==</c> the
///   action's <c>_item</c>. Works for server-owned characters (NPC, host) where the
///   item reference is stable.</description></item>
/// <item><description><b>-1 (player UI hands)</b>: removes the item from the
///   character's hands. Server-owned: uses local <c>HandsController</c>. Remote
///   client: server adds to chest, then fires <see cref="CharacterActions.RemoveFromInventoryAfterStoreClientRpc"/>
///   with -1 to clear the owner's hands.</description></item>
/// <item><description><b>&gt;= 0 (player UI bag slot index)</b>: removes the item
///   from the character's bag at that slot. Same host vs. remote split as above.</description></item>
/// </list>
///
/// <para><b>Multiplayer note (2026-05-14).</b> Bag-inventory contents are NOT
/// replicated by <c>CharacterEquipment._networkEquipment</c> (which only covers
/// weapon / bag-shell / wearable slots). For a remote-client owner, the server-side
/// <c>_bag.Inventory.ItemSlots</c> is a stale empty shadow — the client is the
/// source of truth. The slot-index modes (-1 / &gt;=0) accept a reconstructed
/// <see cref="ItemInstance"/> from the calling RPC's payload and delegate the
/// source removal to the owner via ClientRpc. Mirrors the inverse pattern in
/// <c>CharacterAction_BuyFromShop.DeliverToCustomer</c> and
/// <c>CharacterTakeFromFurnitureAction.OnApplyEffect</c>.</para>
/// </summary>
public class CharacterStoreInFurnitureAction : CharacterAction
{
    /// <summary>Source-resolution sentinel for the legacy "lookup by ItemInstance reference" path used by GOAP NPC deposits.</summary>
    public const int SourceLegacyByReference = -2;
    /// <summary>Source-resolution sentinel for "remove from the character's hands".</summary>
    public const int SourceHands = -1;

    private readonly ItemInstance _item;
    private readonly StorageFurniture _furniture;
    private readonly int _sourceSlotIndex;
    private bool _stored;

    /// <summary>True after a successful slot transfer in <see cref="OnApplyEffect"/>. The
    /// caller (typically a GOAP action) reads this in <c>OnActionFinished</c> to decide
    /// whether to update the building's logical inventory + dispatcher.</summary>
    public bool Stored => _stored;

    public ItemInstance Item => _item;
    public StorageFurniture Furniture => _furniture;
    public int SourceSlotIndex => _sourceSlotIndex;

    /// <summary>
    /// Legacy constructor — GOAP / NPC deposit path. Server-side, server-owned characters,
    /// the action's <c>_item</c> is the original reference from the worker's inventory.
    /// Source resolution walks the bag and hands looking for an <see cref="ItemInstance"/>
    /// matching by reference.
    /// </summary>
    public CharacterStoreInFurnitureAction(Character character, ItemInstance item, StorageFurniture furniture)
        : this(character, item, furniture, SourceLegacyByReference) { }

    /// <summary>
    /// Player-UI constructor — used by <see cref="StorageFurnitureNetworkSync.RequestStoreFromBagServerRpc"/>
    /// and <see cref="StorageFurnitureNetworkSync.RequestStoreFromHandsServerRpc"/>.
    /// <paramref name="sourceSlotIndex"/> must be one of <see cref="SourceHands"/> (-1)
    /// for hands or a non-negative bag-slot index. The action accepts a reconstructed
    /// <see cref="ItemInstance"/> from the RPC payload (the client is the source of
    /// truth for its own bag/hands contents).
    /// </summary>
    public CharacterStoreInFurnitureAction(Character character, ItemInstance item, StorageFurniture furniture, int sourceSlotIndex)
        : base(character, 0.5f)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _furniture = furniture ?? throw new ArgumentNullException(nameof(furniture));
        _sourceSlotIndex = sourceSlotIndex;
    }

    public override bool CanExecute()
    {
        if (_item == null || _furniture == null) return false;
        if (_furniture.IsLocked) return false;
        if (!_furniture.HasFreeSpaceForItem(_item)) return false;
        return true;
    }

    public override void OnStart()
    {
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null) animator.SetTrigger("Trigger_Drop");
    }

    public override void OnApplyEffect()
    {
        // Re-validate at apply time: between OnStart and OnApplyEffect another worker
        // may have filled the last compatible slot, or the lock state may have flipped.
        if (_furniture == null || _furniture.IsLocked || !_furniture.HasFreeSpaceForItem(_item))
        {
            Debug.LogWarning($"<color=orange>[StoreInFurniture]</color> {character?.CharacterName} aborted store: " +
                             $"furniture={_furniture?.FurnitureName} locked={_furniture?.IsLocked} " +
                             $"hasSpace={_furniture?.HasFreeSpaceForItem(_item)}.");
            return;
        }

        var actions = character.CharacterActions;
        bool isRemoteClientOwned = actions != null && actions.IsServer && character.IsSpawned && !character.IsOwnedByServer;

        // --- Remote-client (player UI from a non-host peer) ----------------------------
        // Source state lives on the owning client; the server only knows the item via the
        // RPC payload that produced _item. Add to chest server-side (replicates), then
        // tell the owner to remove from its local source by slot index / hands.
        if (isRemoteClientOwned)
        {
            if (_sourceSlotIndex < SourceHands)
            {
                Debug.LogError($"<color=red>[StoreInFurniture]</color> Remote-client store on {character.CharacterName} requires a slot-index source (got legacy by-reference). Aborting.");
                return;
            }

            if (!_furniture.AddItem(_item))
            {
                Debug.LogError($"<color=red>[StoreInFurniture]</color> {character.CharacterName} could not insert {_item.CustomizedName} into {_furniture.FurnitureName} after validation (race). Owner client keeps the item.");
                return;
            }

            actions.RemoveFromInventoryAfterStoreClientRpc(_sourceSlotIndex);
            _stored = true;
            Debug.Log($"<color=green>[StoreInFurniture]</color> {character.CharacterName} stored {_item.CustomizedName} in {_furniture.FurnitureName} (routed remove via ClientRpc to owner client {character.OwnerClientId}, source={(_sourceSlotIndex == SourceHands ? "hands" : "bag[" + _sourceSlotIndex + "]")}).");
            return;
        }

        // --- Host / NPC / offline — server-side bag/hands is the source of truth ------

        // Player-UI host bag path: look up the slot, remove its ItemInstance precisely.
        if (_sourceSlotIndex >= 0)
        {
            var equip = character.CharacterEquipment;
            if (equip != null && equip.HaveInventory())
            {
                var inv = equip.GetInventory();
                if (inv != null && _sourceSlotIndex < inv.ItemSlots.Count)
                {
                    var slot = inv.ItemSlots[_sourceSlotIndex];
                    if (slot != null && !slot.IsEmpty() && slot.ItemInstance != null)
                    {
                        var slotItem = slot.ItemInstance;
                        if (inv.RemoveItem(slotItem, character))
                        {
                            if (_furniture.AddItem(slotItem))
                            {
                                _stored = true;
                                Debug.Log($"<color=green>[StoreInFurniture]</color> {character.CharacterName} stored {slotItem.CustomizedName} in {_furniture.FurnitureName} (host bag[{_sourceSlotIndex}]).");
                                return;
                            }
                            // Insertion failed after extraction — restore to the bag so nothing is lost.
                            inv.AddItem(slotItem, character);
                            Debug.LogError($"<color=red>[StoreInFurniture]</color> {character.CharacterName} could not insert {slotItem.CustomizedName} into {_furniture.FurnitureName} (host bag path); restored to bag.");
                            return;
                        }
                    }
                }
            }
            Debug.LogWarning($"<color=orange>[StoreInFurniture]</color> {character.CharacterName} bag[{_sourceSlotIndex}] empty / out of range on the host store path.");
            return;
        }

        // Player-UI host hands path.
        if (_sourceSlotIndex == SourceHands)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.IsCarrying)
            {
                var handItem = hands.CarriedItem;
                if (handItem != null && _furniture.AddItem(handItem))
                {
                    hands.DropCarriedItem(); // clears hands + held visual; does not spawn a WorldItem
                    _stored = true;
                    Debug.Log($"<color=green>[StoreInFurniture]</color> {character.CharacterName} stored {handItem.CustomizedName} in {_furniture.FurnitureName} (host hands).");
                    return;
                }
            }
            Debug.LogWarning($"<color=orange>[StoreInFurniture]</color> {character.CharacterName} hands empty on the host store-from-hands path.");
            return;
        }

        // Legacy by-reference path — GOAP / NPC deposits (3-arg ctor).
        bool removed = false;
        var legacyEquip = character.CharacterEquipment;
        if (legacyEquip != null && legacyEquip.HaveInventory())
        {
            if (legacyEquip.GetInventory().RemoveItem(_item, character))
                removed = true;
        }
        if (!removed)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.CarriedItem == _item)
            {
                // DropCarriedItem here only clears hands + destroys the held visual.
                // It does NOT spawn a WorldItem (that's CharacterDropItem.ExecutePhysicalDrop).
                hands.DropCarriedItem();
                removed = true;
            }
        }

        if (!removed)
        {
            Debug.LogWarning($"<color=orange>[StoreInFurniture]</color> {character.CharacterName} did not own {_item.CustomizedName} (inventory + hands both miss). Skipping slot insert.");
            return;
        }

        if (!_furniture.AddItem(_item))
        {
            // Slot insertion failed after we already removed from worker — return to hands
            // so the item isn't lost. The caller can fall back to a zone drop.
            Debug.LogError($"<color=red>[StoreInFurniture]</color> {character.CharacterName} could not insert {_item.CustomizedName} into {_furniture.FurnitureName} after removal. Returning to hands.");
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            hands?.CarryItem(_item);
            return;
        }

        _stored = true;
        Debug.Log($"<color=green>[StoreInFurniture]</color> {character.CharacterName} stored {_item.CustomizedName} in {_furniture.FurnitureName}.");
    }
}
