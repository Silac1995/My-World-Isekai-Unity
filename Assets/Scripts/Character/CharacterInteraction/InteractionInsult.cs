using UnityEngine;
using System.Linq;

public class InteractionInsult : ICharacterInteractionAction
{
    private readonly string[] _insults = {
        "You're a disgrace!",
        "I've seen goblins with better manners.",
        "Your presence is offensive.",
        "Is that your face or did a horse kick you?",
        "You smell like a rotting zombie."
    };

    public void Execute(Character source, Character target)
    {
        Debug.Log($"<color=red>[Insult]</color> {source.CharacterName} insulte {target.CharacterName}!");

        // 1. Diminution de la relation (Unilatéral : la cible apprécie moins la source)
        if (target.CharacterRelation != null)
        {
            target.CharacterRelation.UpdateRelation(source, -5);
        }

        // 2. Shout
        if (source.CharacterSpeech != null)
        {
            string randomInsult = _insults[Random.Range(0, _insults.Length)];
            source.CharacterSpeech.Say(randomInsult);
        }

        // --- IMPACT SUR LES BESOINS ---
        // Insulter peut être un défouloir (ex: réduit la frustration ou augmente un peu le social)
        var sourceSocial = source.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
        if (sourceSocial != null) sourceSocial.IncreaseValue(10f);

        Debug.Log($"<color=red>[Insult]</color> Relation dégradée entre {source.CharacterName} et {target.CharacterName}.");
    }
}
