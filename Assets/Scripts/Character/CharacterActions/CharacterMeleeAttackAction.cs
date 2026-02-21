using UnityEngine;

public class CharacterMeleeAttackAction : CharacterCombatAction
{
    public CharacterMeleeAttackAction(Character character) : base(character, 0.8f)
    {
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            // Récupération dynamique de la durée réelle de l'animation + buffer de sécurité
            this.Duration = animHandler.GetMeleeAttackDuration() + 0.1f;
        }
    }

    public override void OnStart()
    {
        base.OnStart();

        // --- SHOUT ---
        // On dclenche un cri de guerre au dbut de l'action
        character.CharacterSpeech?.Say("I will fuck you up!");

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
