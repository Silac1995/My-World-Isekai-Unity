using UnityEngine;

public class InteractionTalk : ICharacterInteractionAction
{
    public void Execute(Character source, Character target)
    {
        Debug.Log($"{source.CharacterName} discute avec {target.CharacterName}...");

        // Note : On ne ferme pas forcément l'interaction ici 
        // pour permettre de choisir une autre option après.
    }
}