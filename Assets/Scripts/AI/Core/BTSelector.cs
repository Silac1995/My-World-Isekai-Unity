using System.Collections.Generic;

namespace MWI.AI
{
    /// <summary>
    /// Selector (OR logique) : essaie chaque enfant dans l'ordre.
    /// Retourne Success dès qu'un enfant réussit ou Running.
    /// Retourne Failure si TOUS les enfants échouent.
    /// 
    /// IMPORTANT: Si un enfant de rang supérieur redevient valide alors qu'un enfant
    /// de rang inférieur était Running, le Selector abort l'enfant inférieur et
    /// bascule sur le supérieur (preemption / priorité dynamique).
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
                    // Si un enfant de rang supérieur prend le relais,
                    // on abort l'ancien enfant qui était Running
                    if (_lastRunningIndex != -1 && _lastRunningIndex != i)
                    {
                        _children[_lastRunningIndex].Abort(bb);
                    }
                    _lastRunningIndex = i;
                    return BTNodeStatus.Running;
                }

                if (status == BTNodeStatus.Success)
                {
                    // Abort l'ancien Running s'il y en avait un
                    if (_lastRunningIndex != -1 && _lastRunningIndex != i)
                    {
                        _children[_lastRunningIndex].Abort(bb);
                    }
                    _lastRunningIndex = -1;
                    return BTNodeStatus.Success;
                }

                // Failure → on passe à l'enfant suivant
            }

            // Tous les enfants ont échoué
            if (_lastRunningIndex != -1)
            {
                _children[_lastRunningIndex].Abort(bb);
                _lastRunningIndex = -1;
            }
            return BTNodeStatus.Failure;
        }

        protected override void OnExit(Blackboard bb)
        {
            // Cleanup : abort tout enfant encore running
            if (_lastRunningIndex != -1)
            {
                _children[_lastRunningIndex].Abort(bb);
                _lastRunningIndex = -1;
            }
        }
    }
}
