using UnityEngine;
using UnityEngine.TextCore.Text;

public class CharacterDropItem : CharacterAction
{
    private ItemInstance _itemInstance;

    // On donne une petite durée de 0.5s pour l'animation de lâcher
    public CharacterDropItem(Character character, ItemInstance item) : base(character, 0.5f)
    {
        _itemInstance = item ?? throw new System.ArgumentNullException(nameof(item));
    }

    public override void OnStart()
    {
        // Lancer une animation de jet/drop si elle existe
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null) animator.SetTrigger("Trigger_Drop");

        Debug.Log($"{character.CharacterName} prépare l'abandon de {_itemInstance.CustomizedName}.");
    }

    public override void OnApplyEffect()
    {
        // C'est ici qu'on retire l'item de l'inventaire et qu'on le fait apparaître au sol
        if (character.CharacterEquipment.GetInventory().RemoveItem(_itemInstance, character))
        {
            // Code pour faire apparaître l'objet physique dans le monde à la position du perso
            // ItemManager.Instance?.SpawnWorldItem(_itemInstance, character.transform.position);
            Debug.Log($"{_itemInstance.CustomizedName} a été jeté au sol.");
        }
    }
}