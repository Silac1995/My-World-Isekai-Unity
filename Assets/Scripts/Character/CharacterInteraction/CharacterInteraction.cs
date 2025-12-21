using UnityEngine;

public class CharacterInteraction
{
    private Character character;
    private Character currentInteractionTarget;

    public CharacterInteraction(Character character)
    {
        this.character = character;
    }

    /// <summary>
    /// Starts an interaction with the given target character without performing an action yet.
    /// </summary>
    public void StartInteractionWith(Character target)
    {
        if (target == null)
        {
            Debug.LogWarning($"{character.name} tried to start interaction, but target is null.");
            return;
        }

        if (!character.IsFree())
        {
            Debug.LogWarning($"{character.name} cannot start interaction because they are busy.");
            return;
        }

        if (!target.IsFree())
        {
            Debug.LogWarning($"{target.name} cannot be interacted with because they are busy.");
            return;
        }

        currentInteractionTarget = target;
        target.CharacterInteraction.currentInteractionTarget = character;

        Debug.Log($"{character.CharacterName} started interaction with {target.CharacterName}.");
    }


    /// <summary>
    /// Executes a given interaction action with a target character.
    /// </summary>
    public void PerformInteraction(ICharacterInteractionAction action, Character target)
    {
        if (action == null || target == null)
        {
            Debug.LogWarning($"{character.name} interaction failed: action or target is null.");
            return;
        }

        StartInteractionWith(target);
        action.Execute(character, target);
    }

    /// <summary>
    /// Returns true if currently interacting with a character.
    /// </summary>
    public bool IsInInteraction()
    {
        return currentInteractionTarget != null;
    }

    /// <summary>
    /// Returns the character currently being interacted with.
    /// </summary>
    public Character GetCharacterInteractionWith()
    {
        return currentInteractionTarget;
    }

    /// <summary>
    /// Ends the current interaction and also tells the target to end theirs.
    /// </summary>
    public void EndInteraction()
    {
        if (currentInteractionTarget != null)
        {
            Debug.Log($"{character.name} stopped interacting with {currentInteractionTarget.name}.");

            // Tell the other character to end interaction too
            if (currentInteractionTarget.CharacterInteraction.IsInInteraction() &&
                currentInteractionTarget.CharacterInteraction.GetCharacterInteractionWith() == character)
            {
                currentInteractionTarget.CharacterInteraction.ForceEndInteractionOnly();
            }

            currentInteractionTarget = null;
        }
    }

    /// <summary>
    /// Ends this character's interaction without calling back to the other (used internally to avoid recursion).
    /// </summary>
    private void ForceEndInteractionOnly()
    {
        if (currentInteractionTarget != null)
        {
            Debug.Log($"{character.name} stopped interacting with {currentInteractionTarget.name} (forced end).");
            currentInteractionTarget = null;
        }
    }
}
