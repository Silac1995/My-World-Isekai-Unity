using UnityEngine;

namespace MWI.AI
{
    public class BTCond_HasLegacyBehaviour : BTNode
    {
        private NPCController _npcController;

        protected override void OnEnter(Blackboard blackboard)
        {
            if (_npcController == null)
            {
                var character = blackboard.Self;
                if (character != null)
                {
                    _npcController = character.Controller as NPCController;
                }
            }
        }

        protected override BTNodeStatus OnExecute(Blackboard blackboard)
        {
            if (_npcController == null) return BTNodeStatus.Failure;

            if (_npcController.CurrentBehaviour != null)
            {
                return BTNodeStatus.Success;
            }

            return BTNodeStatus.Failure;
        }

        protected override void OnExit(Blackboard blackboard) { }
    }
}
