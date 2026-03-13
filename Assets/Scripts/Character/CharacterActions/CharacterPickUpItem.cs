using UnityEngine;

public class CharacterPickUpItem : CharacterAction
{
    private ItemInstance _item;
    private GameObject _worldObject;

    public CharacterPickUpItem(Character character, ItemInstance item, GameObject worldObject) : base(character, 3f)
    {
        _item = item;
        _worldObject = worldObject;

        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            float duration = animHandler.GetCachedDuration("Female_Humanoid_Pickup_from_ground_00");
            if (duration > 0)
            {
                this.Duration = duration;
            }
        }
    }

    public override void OnStart()
    {
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler?.Animator != null)
        {
            animHandler.Animator.SetTrigger(CharacterAnimator.ActionTrigger);
        }

        if (_worldObject != null)
        {
            var rb = _worldObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }
    }

    public override void OnApplyEffect()
    {
        if (character.CharacterEquipment != null && character.CharacterEquipment.PickUpItem(_item))
        {
            if (_worldObject != null)
            {
                Object.Destroy(_worldObject);
            }
        }
        else
        {
            Debug.LogWarning($"[Action] Pickup failed for {_item.CustomizedName}. Item remains on ground.");
        }
    }

    public override bool CanExecute()
    {
        if (character.CharacterEquipment == null) return false;

        if (!character.CharacterEquipment.CanCarryItemAnyMore(_item))
        {
            Debug.LogWarning($"[Action] {character.CharacterName} cannot carry {_item.CustomizedName} (Inventory full or No Bag).");
            return false;
        }

        return true;
    }
}
