using System.Collections.Generic;

namespace MWI.AI
{
    /// <summary>
    /// Selector (logical OR): tries each child in order.
    /// Returns Success as soon as a child succeeds, or Running.
    /// Returns Failure if ALL children fail.
    ///
    /// IMPORTANT: If a higher-priority child becomes valid again while a lower-priority
    /// child was Running, the Selector aborts the lower child and switches to the
    /// higher one (preemption / dynamic priority).
    /// </summary>
    public class BTSelector : BTNode
    {
        private List<BTNode> _children = new List<BTNode>();
        private int _lastRunningIndex = -1;

        public BTSelector(params BTNode[] children)
        {
            _children.AddRange(children);
        }

        public void AddChild(BTNode child)
        {
            _children.Add(child);
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            for (int i = 0; i < _children.Count; i++)
            {
                BTNodeStatus status = _children[i].Execute(bb);

                if (status == BTNodeStatus.Running)
                {
                    // If a higher-priority child takes over,
                    // abort the previous child that was Running
                    if (_lastRunningIndex != -1 && _lastRunningIndex != i)
                    {
                        _children[_lastRunningIndex].Abort(bb);
                    }
                    _lastRunningIndex = i;
                    return BTNodeStatus.Running;
                }

                if (status == BTNodeStatus.Success)
                {
                    // Abort the previous Running child if there was one
                    if (_lastRunningIndex != -1 && _lastRunningIndex != i)
                    {
                        _children[_lastRunningIndex].Abort(bb);
                    }
                    _lastRunningIndex = -1;
                    return BTNodeStatus.Success;
                }

                // Failure -> move on to the next child
            }

            // All children failed
            if (_lastRunningIndex != -1)
            {
                _children[_lastRunningIndex].Abort(bb);
                _lastRunningIndex = -1;
            }
            return BTNodeStatus.Failure;
        }

        protected override void OnExit(Blackboard bb)
        {
            // Cleanup: abort any child still running
            if (_lastRunningIndex != -1)
            {
                _children[_lastRunningIndex].Abort(bb);
                _lastRunningIndex = -1;
            }
        }
    }
}
