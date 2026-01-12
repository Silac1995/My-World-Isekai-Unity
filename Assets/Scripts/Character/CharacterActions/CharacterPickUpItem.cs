using UnityEngine;

public class CharacterPickUpItem : CharacterAction
{
    private ItemInstance _item;
    private GameObject _worldObject; // On stocke l'objet à détruire

    public CharacterPickUpItem(Character character, ItemInstance item, GameObject worldObject) : base(character, 0.5f)
    {
        _item = item;
        _worldObject = worldObject;
    }

    public override void OnStart()
    {
        var animator = character.CharacterVisual.CharacterAnimator.Animator;
        if (animator != null) animator.SetTrigger("Trigger_pickUpItem");
    }

    public override void OnApplyEffect()
    {
        var inventory = character.CharacterEquipment.GetInventory();

        if (inventory != null && inventory.AddItem(_item))
        {
            // Le ramassage a réussi, on détruit l'objet au sol
            if (_worldObject != null)
            {
                Object.Destroy(_worldObject);
            }
        }
        else
        {
            Debug.LogWarning("Ramassage échoué : l'objet reste au sol.");
        }
    }
}