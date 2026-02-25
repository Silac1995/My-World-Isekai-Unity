using System.Collections.Generic;

namespace MWI.AI
{
    /// <summary>
    /// Sequence (AND logique) : exécute chaque enfant dans l'ordre.
    /// Retourne Failure dès qu'un enfant échoue.
    /// Retourne Success quand TOUS les enfants réussissent.
    /// Retourne Running si l'enfant courant est Running.
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

                // Success → passer au suivant
                _currentIndex++;
            }

            // Tous les enfants ont réussi
            _currentIndex = 0;
            return BTNodeStatus.Success;
        }

        protected override void OnExit(Blackboard bb)
        {
            // Abort l'enfant courant s'il était encore Running
            if (_currentIndex < _children.Count)
            {
                _children[_currentIndex].Abort(bb);
            }
            _currentIndex = 0;
        }
    }
}
