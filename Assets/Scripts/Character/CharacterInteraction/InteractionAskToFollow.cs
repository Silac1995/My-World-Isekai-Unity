using UnityEngine;

public class InteractionAskToFollow : ICharacterInteractionAction
{
    public void Execute(Character source, Character target)
    {
        if (source == null || target == null) return;

        var npcController = target.GetComponent<NPCController>();
        if (npcController == null) return;

        if (source == target)
        {
            // Si on veut qu'il arrête de nous suivre, on pourrait faire un Pop 
            // ou un Reset de la pile vers le Wander.
            npcController.ResetStackTo(new WanderBehaviour(npcController));
            return;
        }

        // On utilise Push pour ajouter le comportement de suivi au sommet.
        // Désormais, si une interruption (faim, objet à ramasser) survient, 
        // on fera un Push par-dessus, et au Pop, il reviendra à ce Follow.
        npcController.PushBehaviour(new FollowTargetBehaviour(source, target.Controller.Agent));

        Debug.Log($"{target.CharacterName} is now following {source.CharacterName}");
    }
}
