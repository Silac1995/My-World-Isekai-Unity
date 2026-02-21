using UnityEngine;

public class CharacterMeleeAttackAction : CharacterCombatAction
{
    public CharacterMeleeAttackAction(Character character) : base(character, 0.8f)
    {
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            // Récupération dynamique de la durée réelle de l'animation
            this.Duration = animHandler.GetMeleeAttackDuration();
        }
    }

    public override void OnStart()
    {
        base.OnStart();
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            animHandler.PlayMeleeAttack();
        }
    }

    public override void OnApplyEffect()
    {
        base.OnApplyEffect();
        // Les dgts sont grs par les Animation Events (SpawnCombatStyleAttackInstance)
    }
}
