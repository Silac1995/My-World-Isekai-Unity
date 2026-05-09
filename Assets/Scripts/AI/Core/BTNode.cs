namespace MWI.AI
{
    /// <summary>
    /// Abstract base class for all Behaviour Tree nodes.
    /// Handles the Enter/Execute/Exit lifecycle automatically.
    /// </summary>
    public abstract class BTNode
    {
        private bool _isRunning = false;

        /// <summary>
        /// Called once when the node starts executing.
        /// </summary>
        protected virtual void OnEnter(Blackboard bb) { }

        /// <summary>
        /// Called when the node stops (success, failure, or interruption).
        /// </summary>
        protected virtual void OnExit(Blackboard bb) { }

        /// <summary>
        /// Main node logic. Returns Running, Success, or Failure.
        /// </summary>
        protected abstract BTNodeStatus OnExecute(Blackboard bb);

        /// <summary>
        /// Main entry point. Handles OnEnter/OnExit automatically.
        /// </summary>
        public BTNodeStatus Execute(Blackboard bb)
        {
            if (!_isRunning)
            {
                OnEnter(bb);
                _isRunning = true;
            }

            BTNodeStatus status = OnExecute(bb);

            if (status != BTNodeStatus.Running)
            {
                OnExit(bb);
                _isRunning = false;
            }

            return status;
        }

        /// <summary>
        /// Forces the node to stop (e.g. when a Selector switches branches).
        /// </summary>
        public void Abort(Blackboard bb)
        {
            if (_isRunning)
            {
                OnExit(bb);
                _isRunning = false;
            }
        }

        public bool IsRunning => _isRunning;
    }
}
