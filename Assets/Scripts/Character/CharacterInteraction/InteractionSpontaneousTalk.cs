using UnityEngine;

/// <summary>
/// A spontaneous action where a character says something without formally
/// entering the dialogue state machine.
/// </summary>
public class InteractionSpontaneousTalk : ICharacterInteractionAction
{
    public void Execute(Character source, Character target)
    {
        if (source.CharacterSpeech != null)
        {
            source.CharacterSpeech.Say("Hello!");
        }
        else
        {
            Debug.LogWarning($"<color=orange>[SpontaneousTalk]</color> {source.CharacterName} tries to talk but has no CharacterSpeech component.");
        }
    }
}
