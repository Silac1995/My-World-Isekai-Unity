using System.Collections.Generic;

namespace MWI.AI
{
    /// <summary>
    /// Sequence (logical AND): executes each child in order.
    /// Returns Failure as soon as a child fails.
    /// Returns Success when ALL children succeed.
    /// Returns Running if the current child is Running.
    /// </summary>
    public class BTSequence : BTNode
    {
        private List<BTNode> _children = new List<BTNode>();
        private int _currentIndex = 0;

        public BTSequence(params BTNode[] children)
        {
            _children.AddRange(children);
        }

        public void AddChild(BTNode child)
        {
            _children.Add(child);
        }

        protected override void OnEnter(Blackboard bb)
        {
            _currentIndex = 0;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            while (_currentIndex < _children.Count)
            {
                BTNodeStatus status = _children[_currentIndex].Execute(bb);

                if (status == BTNodeStatus.Running)
                    return BTNodeStatus.Running;

                if (status == BTNodeStatus.Failure)
                {
                    _currentIndex = 0;
                    return BTNodeStatus.Failure;
                }

                // Success -> move on to the next
                _currentIndex++;
            }

            // All children succeeded
            _currentIndex = 0;
            return BTNodeStatus.Success;
        }

        protected override void OnExit(Blackboard bb)
        {
            // Abort the current child if it was still Running
            if (_currentIndex < _children.Count)
            {
                _children[_currentIndex].Abort(bb);
            }
            _currentIndex = 0;
        }
    }
}
