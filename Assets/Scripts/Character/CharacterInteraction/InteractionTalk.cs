using UnityEngine;
using System.Linq;

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

        // --- SATISFACTION DU BESOIN SOCIAL ---
        // On cherche le besoin social sur les deux participants via leur manager de besoins
        var sourceSocial = source.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
        if (sourceSocial != null) sourceSocial.IncreaseValue(40f);

        var targetSocial = target.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
        if (targetSocial != null) targetSocial.IncreaseValue(40f);

        Debug.Log($"<color=lightblue>[Talk]</color> Besoin social satisfait pour {source.CharacterName} et {target.CharacterName}.");
    }
}
