using System;

namespace MWI.AI
{
    /// <summary>
    /// Conditional decorator: checks a condition before executing the child.
    /// If the condition is true -> executes the child and returns its status.
    /// If the condition is false -> returns Failure without executing the child.
    /// </summary>
    public class BTCondition : BTNode
    {
        private Func<Blackboard, bool> _condition;
        private BTNode _child;

        public BTCondition(Func<Blackboard, bool> condition, BTNode child)
        {
            _condition = condition;
            _child = child;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            if (_condition(bb))
            {
                return _child.Execute(bb);
            }

            // The condition failed: if the child was running, abort it
            if (_child.IsRunning)
            {
                _child.Abort(bb);
            }

            return BTNodeStatus.Failure;
        }

        protected override void OnExit(Blackboard bb)
        {
            _child.Abort(bb);
        }
    }
}
