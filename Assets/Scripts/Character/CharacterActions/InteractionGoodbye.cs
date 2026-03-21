using UnityEngine;

/// <summary>
/// A dialogue action that gracefully ends an interaction.
/// When executed, the source character says goodbye and the interaction is terminated.
/// </summary>
public class InteractionGoodbye : ICharacterInteractionAction
{
    public void Execute(Character source, Character target)
    {
        Debug.Log($"<color=cyan>[Dialogue]</color> {source.CharacterName} says goodbye to {target.CharacterName}.");

        if (source.CharacterSpeech != null)
        {
            source.CharacterSpeech.Say("Goodbye!");
        }

        // Forcefully end the interaction from the source's side
        source.CharacterInteraction.EndInteraction();
    }
}
