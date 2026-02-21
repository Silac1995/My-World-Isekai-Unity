using UnityEngine;

public abstract class CharacterCombatAction : CharacterAction
{
    protected CharacterCombatAction(Character character, float duration = 0f) 
        : base(character, duration)
    {
    }

    public override void OnStart()
    {
        // On ne met pas IsDoingAction à true car cela déclenche une animation générique
        // qui entre en conflit avec l'animation de combat spécifique (MeleeAttack, etc).
    }

    public override void OnApplyEffect()
    {
        // Pas de désactivation de IsDoingAction ici non plus.
    }
}
