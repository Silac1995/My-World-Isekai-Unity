using UnityEngine;
using UnityEngine.TextCore.Text;

public class CharacterDropItem : CharacterAction
{
    private ItemInstance _itemInstance;
    private bool _freezeOnGround;

    public CharacterDropItem(Character character, ItemInstance item, bool freezeOnGround = false) : base(character, 0.5f)
    {
        _itemInstance = item ?? throw new System.ArgumentNullException(nameof(item));
        _freezeOnGround = freezeOnGround;
    }

    public override void OnStart()
    {
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null) animator.SetTrigger("Trigger_Drop");

        Debug.Log($"{character.CharacterName} prepare le drop.");
    }

    public override void OnApplyEffect()
    {
        bool removed = false;
        
        var equip = character.CharacterEquipment;
        if (equip != null && equip.HaveInventory())
        {
            if (equip.GetInventory().RemoveItem(_itemInstance, character))
            {
                removed = true;
            }
        }

        if (!removed)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.CarriedItem == _itemInstance)
            {
                hands.DropCarriedItem();
                removed = true;
            }
        }

        if (removed)
        {
            Vector3 dropPos = character.transform.position + Vector3.up * 1.5f;
            Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            WorldItem spawnedItem = WorldItem.SpawnWorldItem(_itemInstance, dropPos + offset);
            
            if (spawnedItem != null)
            {
                spawnedItem.FreezeOnGround = _freezeOnGround;
            }

            Debug.Log($"Item {_itemInstance.ItemSO.ItemName} lache physiquement.");
        }
    }
}
