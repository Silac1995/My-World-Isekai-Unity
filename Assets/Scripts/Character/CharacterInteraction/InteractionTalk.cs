using UnityEngine;
using System.Linq;

public class InteractionTalk : ICharacterInteractionAction
{
    private readonly string[] _dialogues = {
        "Beautiful day, isn't it?",
        "Have you heard any interesting rumors lately?",
        "I was just thinking about the local history...",
        "How are things going for you?",
        "It's good to see a friendly face."
    };

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

        // 3. Shout/Dialogue
        if (source.CharacterSpeech != null)
        {
            string randomTalk = _dialogues[Random.Range(0, _dialogues.Length)];
            source.CharacterSpeech.Say(randomTalk);
        }

        // --- SATISFACTION DU BESOIN SOCIAL ---
        var sourceSocial = source.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
        if (sourceSocial != null) sourceSocial.IncreaseValue(40f);

        var targetSocial = target.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
        if (targetSocial != null) targetSocial.IncreaseValue(40f);

        Debug.Log($"<color=lightblue>[Talk]</color> Besoin social satisfait pour {source.CharacterName} et {target.CharacterName}.");
    }
}
