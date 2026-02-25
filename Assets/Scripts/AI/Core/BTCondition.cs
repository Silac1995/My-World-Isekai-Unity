using System;

namespace MWI.AI
{
    /// <summary>
    /// Decorator conditionnel : vérifie une condition avant d'exécuter l'enfant.
    /// Si la condition est true → exécute l'enfant et retourne son status.
    /// Si la condition est false → retourne Failure sans exécuter l'enfant.
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

            // La condition a échoué : si l'enfant était en cours, on l'abort
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
