using UnityEngine;

public class CharacterEquipAction : CharacterAction
{
    private EquipmentInstance _equipment;

    // On ajoute un délai de 0.8s pour l'action d'équipement
    public CharacterEquipAction(Character character, EquipmentInstance equipment)
        : base(character, 0.8f)
    {
        _equipment = equipment ?? throw new System.ArgumentNullException(nameof(equipment));
    }

    public override void OnStart()
    {
        if (_equipment == null) { Finish(); return; }

        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null)
        {
            // On lance la boucle
            animator.SetBool(CharacterAnimator.IsDoingAction, true);
        }

        Debug.Log($"{character.CharacterName} prépare l'équipement...");
    }

    public override void OnApplyEffect()
    {
        character.CharacterEquipment.Equip(_equipment);

        // On coupe la boucle ici car l'effet est appliqué et l'action se termine
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null)
        {
            animator.SetBool(CharacterAnimator.IsDoingAction, false);
        }
    }
}