namespace MWI.AI
{
    /// <summary>
    /// Condition : vérifie si le NPC a un ordre actif et valide.
    /// Si oui, exécute l'ordre directement (pas besoin d'enfant, c'est une leaf node).
    /// </summary>
    public class BTCond_HasOrder : BTNode
    {
        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            NPCOrder order = bb.Get<NPCOrder>(Blackboard.KEY_CURRENT_ORDER);

            if (order == null || order.IsComplete || !order.IsValid())
            {
                bb.Remove(Blackboard.KEY_CURRENT_ORDER);
                return BTNodeStatus.Failure;
            }

            return order.Execute(bb.Self);
        }

        protected override void OnExit(Blackboard bb)
        {
            // Si on sort de ce node (ex: ordre annulé par preemption), on cancel l'ordre
            NPCOrder order = bb.Get<NPCOrder>(Blackboard.KEY_CURRENT_ORDER);
            if (order != null && !order.IsComplete)
            {
                order.Cancel(bb.Self);
                bb.Remove(Blackboard.KEY_CURRENT_ORDER);
            }
        }
    }
}
