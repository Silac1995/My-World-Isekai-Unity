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

        // 2. On termine l'interaction (nettoie les behaviours d'interaction)
        source.CharacterInteraction.EndInteraction();

        // 3. LA CORRECTION : On suit 'source' (le joueur), pas 'target' !
        // On utilise SetBehaviour pour être sûr de vider tout résidu qui forcerait un Stop.
        npcController.SetBehaviour(new FollowTargetBehaviour(source, 3f));

        Debug.Log($"<color=green>[AI]</color> {target.CharacterName} suit maintenant {source.CharacterName}");
    }
}