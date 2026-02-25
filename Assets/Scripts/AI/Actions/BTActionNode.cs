using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Base class pour les Action nodes qui wrappent un IAIBehaviour existant.
    /// Gère le lifecycle Act()/Exit() et traduit IsFinished en BTNodeStatus.
    /// </summary>
    public abstract class BTActionNode : BTNode
    {
        protected IAIBehaviour _behaviour;

        protected abstract IAIBehaviour CreateBehaviour(Blackboard bb);

        protected override void OnEnter(Blackboard bb)
        {
            _behaviour = CreateBehaviour(bb);
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            if (_behaviour == null)
                return BTNodeStatus.Failure;

            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            if (_behaviour.IsFinished)
                return BTNodeStatus.Success;

            _behaviour.Act(self);

            return _behaviour.IsFinished ? BTNodeStatus.Success : BTNodeStatus.Running;
        }

        protected override void OnExit(Blackboard bb)
        {
            if (_behaviour != null)
            {
                Character self = bb.Self;
                if (self != null)
                    _behaviour.Exit(self);
                _behaviour = null;
            }
        }
    }
}
