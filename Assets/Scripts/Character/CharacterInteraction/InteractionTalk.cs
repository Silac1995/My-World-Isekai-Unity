using UnityEngine;

public class InteractionTalk : ICharacterInteractionAction
{
    public void Execute(Character source, Character target)
    {
        Debug.Log($"<color=lightblue>[Talk]</color> {source.CharacterName} discute avec {target.CharacterName}...");

        // 1. Augmenter l'avis de la source sur la cible
        if (source.CharacterRelation != null)
        {
            source.CharacterRelation.UpdateRelation(target, 1);
        }

        // 2. Augmenter l'avis de la cible sur la source (Réciprocité)
        if (target.CharacterRelation != null)
        {
            target.CharacterRelation.UpdateRelation(source, 1);
        }

        // Note : On ne ferme pas l'interaction ici pour permettre d'autres choix.
    }
}