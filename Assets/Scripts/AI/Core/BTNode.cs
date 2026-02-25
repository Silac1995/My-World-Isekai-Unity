namespace MWI.AI
{
    /// <summary>
    /// Classe abstraite de base pour tous les noeuds du Behaviour Tree.
    /// Gère le lifecycle Enter/Execute/Exit automatiquement.
    /// </summary>
    public abstract class BTNode
    {
        private bool _isRunning = false;

        /// <summary>
        /// Appelé une seule fois quand le node commence à s'exécuter.
        /// </summary>
        protected virtual void OnEnter(Blackboard bb) { }

        /// <summary>
        /// Appelé quand le node s'arrête (succès, échec, ou interruption).
        /// </summary>
        protected virtual void OnExit(Blackboard bb) { }

        /// <summary>
        /// Logique principale du node. Retourne Running, Success, ou Failure.
        /// </summary>
        protected abstract BTNodeStatus OnExecute(Blackboard bb);

        /// <summary>
        /// Point d'entrée principal. Gère automatiquement OnEnter/OnExit.
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
        /// Force l'arrêt du node (ex: quand un Selector change de branche).
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
