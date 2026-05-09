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
/// Server-authoritative. <see cref="StorageFurniture"/> contents are server-only
/// today; <c>OnApplyEffect</c> only runs on the server (or offline mode), so the
/// slot mutation lands in the right place. Clients still play the drop animation
/// via the standard <c>BroadcastActionVisualsClientRpc</c> path.
/// </summary>
public class CharacterStoreInFurnitureAction : CharacterAction
{
    private readonly ItemInstance _item;
    private readonly StorageFurniture _furniture;
    private bool _stored;

    /// <summary>True after a successful slot transfer in <see cref="OnApplyEffect"/>. The
    /// caller (typically a GOAP action) reads this in <c>OnActionFinished</c> to decide
    /// whether to update the building's logical inventory + dispatcher.</summary>
    public bool Stored => _stored;

    public ItemInstance Item => _item;
    public StorageFurniture Furniture => _furniture;

    public CharacterStoreInFurnitureAction(Character character, ItemInstance item, StorageFurniture furniture)
        : base(character, 0.5f)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _furniture = furniture ?? throw new ArgumentNullException(nameof(furniture));
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

        bool removed = false;
        var equip = character.CharacterEquipment;
        if (equip != null && equip.HaveInventory())
        {
            if (equip.GetInventory().RemoveItem(_item, character))
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
