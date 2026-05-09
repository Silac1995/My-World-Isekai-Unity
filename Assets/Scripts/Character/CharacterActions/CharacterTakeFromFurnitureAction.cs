using System;
using UnityEngine;

/// <summary>
/// Mirror of <see cref="CharacterStoreInFurnitureAction"/>. Pulls an
/// <see cref="ItemInstance"/> out of a <see cref="StorageFurniture"/> slot and
/// places it into the character's hands. Used by the outbound logistics path
/// (<c>GoapAction_StageItemForPickup</c>) when a reserved transport instance
/// lives inside a furniture slot rather than as a loose <see cref="WorldItem"/>.
///
/// Server-authoritative — slot mutation only lands on the server. Clients see
/// the standard pickup-style animation through the regular broadcast path.
/// </summary>
public class CharacterTakeFromFurnitureAction : CharacterAction
{
    private readonly ItemInstance _item;
    private readonly StorageFurniture _furniture;
    private readonly bool _preferInventory;
    private bool _taken;

    /// <summary>True after the slot was successfully drained and the item placed in hands or inventory.</summary>
    public bool Taken => _taken;

    public ItemInstance Item => _item;
    public StorageFurniture Furniture => _furniture;

    /// <summary>
    /// Construct a take action. Default <paramref name="preferInventory"/>=false matches
    /// NPC GOAP transport semantics — items are placed in hands so the worker can carry
    /// them somewhere else. The player UI passes <c>true</c> so chest clicks fill the
    /// bag inventory first (mirrors <see cref="CharacterEquipment.PickUpItem"/>) and only
    /// fall through to hands when the bag is full.
    /// </summary>
    public CharacterTakeFromFurnitureAction(Character character, ItemInstance item, StorageFurniture furniture, bool preferInventory = false)
        : base(character, 0.5f)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _furniture = furniture ?? throw new ArgumentNullException(nameof(furniture));
        _preferInventory = preferInventory;
    }

    public override bool CanExecute()
    {
        if (_item == null || _furniture == null) return false;
        if (_furniture.IsLocked) return false;
        // Item must still be in the furniture.
        bool itemPresent = false;
        var slots = _furniture.ItemSlots;
        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].ItemInstance == _item) { itemPresent = true; break; }
            }
        }
        if (!itemPresent) return false;

        // Inventory-first path: succeed if the bag has free space for this item.
        if (_preferInventory)
        {
            var equipment = character.CharacterEquipment;
            if (equipment != null && equipment.HaveInventory())
            {
                var inv = equipment.GetInventory();
                if (inv != null && inv.HasFreeSpaceForItem(_item)) return true;
            }
        }

        // Fallback (and default for NPC paths): hands must be free.
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        return hands != null && hands.AreHandsFree();
    }

    public override void OnStart()
    {
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null) animator.SetTrigger(CharacterAnimator.ActionTrigger);
    }

    public override void OnApplyEffect()
    {
        if (_furniture == null || _furniture.IsLocked)
        {
            Debug.LogWarning($"<color=orange>[TakeFromFurniture]</color> {character?.CharacterName} aborted: furniture={_furniture?.FurnitureName} locked={_furniture?.IsLocked}.");
            return;
        }

        if (!_furniture.RemoveItem(_item))
        {
            Debug.LogWarning($"<color=orange>[TakeFromFurniture]</color> {character.CharacterName} could not extract {_item?.CustomizedName} from {_furniture.FurnitureName} (already gone).");
            return;
        }

        // Inventory-first (player UI path).
        if (_preferInventory)
        {
            var equipment = character.CharacterEquipment;
            if (equipment != null && equipment.HaveInventory())
            {
                var inv = equipment.GetInventory();
                if (inv != null && inv.AddItem(_item, character))
                {
                    _taken = true;
                    Debug.Log($"<color=green>[TakeFromFurniture]</color> {character.CharacterName} took {_item.CustomizedName} from {_furniture.FurnitureName} into bag.");
                    return;
                }
            }
        }

        // Default / fallback: hands.
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.CarryItem(_item))
        {
            _taken = true;
            Debug.Log($"<color=green>[TakeFromFurniture]</color> {character.CharacterName} took {_item.CustomizedName} from {_furniture.FurnitureName} into hands.");
            return;
        }

        // Both bag (when preferred) and hands failed — restore item to chest so it isn't lost.
        Debug.LogError($"<color=red>[TakeFromFurniture]</color> {character.CharacterName} could not place {_item.CustomizedName} in inventory or hands after extraction. Returning to {_furniture.FurnitureName}.");
        _furniture.AddItem(_item);
    }
}
