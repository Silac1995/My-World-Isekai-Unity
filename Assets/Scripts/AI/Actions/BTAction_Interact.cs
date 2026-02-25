namespace MWI.AI
{
    /// <summary>
    /// Wrappe InteractBehaviour + MoveToInteractionBehaviour dans le BT.
    /// Phase 1: MoveToInteraction → Phase 2: Interact.
    /// </summary>
    public class BTAction_Interact : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.CharacterInteraction.IsInteracting) return null;

            Character target = self.CharacterInteraction.CurrentTarget;
            if (target == null) return null;

            var controller = self.Controller as CharacterGameController;
            if (controller == null) return null;

            return new MoveToInteractionBehaviour(controller, target);
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            // Si le MoveToInteraction est fini, on passe à InteractBehaviour
            if (_behaviour != null && _behaviour.IsFinished && self.CharacterInteraction.IsInteracting)
            {
                _behaviour.Exit(self);
                _behaviour = new InteractBehaviour();
            }

            return base.OnExecute(bb);
        }
    }
}
