using UnityEngine;

public class InteractionAskToFollow : ICharacterInteractionAction
{
    public void Execute(Character source, Character target)
    {
        if (source == null || target == null) return;

        var npcController = target.GetComponent<NPCController>();
        if (npcController == null) return;

        // 1. Si on demande d'arrêter (il suit déjà)
        if (npcController.HasBehaviour<FollowTargetBehaviour>())
        {
            npcController.ResetStackTo(new WanderBehaviour(npcController));
            source.CharacterInteraction.EndInteraction();
            return;
        }

        // 2. On termine l'interaction d'abord pour vider le InteractBehaviour de la pile
        source.CharacterInteraction.EndInteraction();

        // 3. On Push le Follow MAINTENANT. 
        // Comme l'interaction est finie, personne ne viendra faire un Pop par dessus.
        npcController.PushBehaviour(new FollowTargetBehaviour(source, target.Controller.Agent, 3f));

        Debug.Log($"<color=green>[AI]</color> {target.CharacterName} suit maintenant {source.CharacterName}");
    }
}