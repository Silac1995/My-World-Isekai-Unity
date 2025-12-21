using UnityEngine;

public class CharacterInteractAction : CharacterAction
{
    private Character target;

    public CharacterInteractAction(Character character, Character target) : base(character)
    {
        this.target = target ?? throw new System.ArgumentNullException(nameof(target));
    }

    public override void PerformAction()
    {
        if (target == null)
        {
            Debug.LogWarning($"{character.CharacterName} tente d'interagir avec une cible inexistante.");
            return;
        }
    }
}
