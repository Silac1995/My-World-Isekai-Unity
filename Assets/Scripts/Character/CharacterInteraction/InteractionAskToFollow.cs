using UnityEngine;

public class InteractionAskToFollow : ICharacterInteractionAction
{
    public void Execute(Character source, Character target)
    {
        if (source == null || target == null)
        {
            Debug.LogWarning("AskToFollow failed: source or target is null.");
            return;
        }

        var npcController = target.GetComponent<NPCController>();
        if (npcController == null)
        {
            Debug.LogWarning($"Target {target.name} is not an NPC, cannot follow.");
            return;
        }

        // Vérifie si le NPC tente de se suivre lui-même
        if (source == target)
        {
            Debug.LogWarning($"{target.name} ne peut pas se suivre lui-même ! Remise en Wander.");
            // Remet le comportement par défaut (wander)
            npcController.SetBehaviour(new WanderBehaviour(npcController));
            return;
        }

        // Changer le comportement du NPC pour Follow
        npcController.SetBehaviour(new FollowTargetBehaviour(source, target.Controller.Agent));
        Debug.Log($"{target.CharacterName} is now following {source.CharacterName}");
    }
}
