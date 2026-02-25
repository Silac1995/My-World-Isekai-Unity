using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Wrappe MoveToTargetBehaviour dans le BT.
    /// </summary>
    public class BTAction_MoveToTarget : BTActionNode
    {
        private GameObject _target;
        private float _speed;
        private System.Action _onArrived;

        public BTAction_MoveToTarget(GameObject target, float speed = 5f, System.Action onArrived = null)
        {
            _target = target;
            _speed = speed;
            _onArrived = onArrived;
        }

        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            Character self = bb.Self;
            NPCController npc = self?.Controller as NPCController;
            if (npc == null || _target == null) return null;
            return new MoveToTargetBehaviour(npc, _target, _speed, _onArrived);
        }
    }
}
