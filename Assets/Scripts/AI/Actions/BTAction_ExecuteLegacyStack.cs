using UnityEngine;

namespace MWI.AI
{
    public class BTAction_ExecuteLegacyStack : BTNode
    {
        private Character _character;
        private NPCController _npcController;
        private bool _initialized = false;

        protected override void OnEnter(Blackboard blackboard)
        {
            if (!_initialized)
            {
                _character = blackboard.Self;
                if (_character != null)
                {
                    _npcController = _character.Controller as NPCController;
                }
                _initialized = true;
            }
        }

        protected override BTNodeStatus OnExecute(Blackboard blackboard)
        {
            if (_npcController == null || _character == null) 
                return BTNodeStatus.Failure;

            var current = _npcController.CurrentBehaviour;
            
            if (current == null)
                return BTNodeStatus.Success; 

            if (current.IsFinished)
            {
                _npcController.PopBehaviour();
                return BTNodeStatus.Success; 
            }

            if (!_npcController.IsFrozen)
            {
                current.Act(_character);
            }

            return BTNodeStatus.Running;
        }

        protected override void OnExit(Blackboard blackboard) { }
    }
}
