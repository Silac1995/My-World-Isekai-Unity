using UnityEngine;

public class CharacterSit : CharacterAction
{
    // On définit une durée, par exemple 1 seconde pour s'asseoir proprement
    public CharacterSit(Character character) : base(character, 1.0f) { }

    public override void OnStart()
    {
        // On récupère l'animator via ta hiérarchie
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null)
        {
            // On lance le trigger de l'animation
            animator.SetTrigger("Trigger_Sit");
        }

        Debug.Log($"{character.CharacterName} commence à s'asseoir.");
    }

    public override void OnApplyEffect()
    {
        // Ici, on valide que le personnage est officiellement assis
        // Par exemple : character.StateMachine.SetState(CharacterState.Sitting);
        Debug.Log($"{character.CharacterName} est maintenant assis.");
    }
}