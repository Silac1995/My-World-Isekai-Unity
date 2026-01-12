using UnityEngine;

public class CharacterUseConsumableAction : CharacterAction
{
    private ConsumableInstance _item;

    // On peut définir une durée (ex: 1.5s pour boire une potion)
    public CharacterUseConsumableAction(Character character, ConsumableInstance item)
        : base(character, 1.5f)
    {
        _item = item;
    }

    public override void OnStart()
    {
        // 1. Lancer l'animation (ex: Boire)
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null)
        {
            animator.SetTrigger("Trigger_Consume");
        }

        Debug.Log($"{character.CharacterName} commence à consommer {_item.CustomizedName}...");
    }

    public override void OnApplyEffect()
    {
        // 2. C'est ici, à la fin du timer, que les PV sont rendus et l'objet retiré
        if (_item != null)
        {
            character.UseConsumable(_item);
            Debug.Log($"Effet de {_item.CustomizedName} appliqué !");
        }
    }
}