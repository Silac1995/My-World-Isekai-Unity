using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Ordre : défendre/suivre une cible.
    /// Le NPC suit la cible et vient en aide si elle est attaquée.
    /// </summary>
    public class OrderDefendTarget : NPCOrder
    {
        private Character _target;
        private float _followDistance;

        public override NPCOrderType OrderType => NPCOrderType.DefendTarget;
        public Character Target => _target;

        public OrderDefendTarget(Character target, float followDistance = 5f)
        {
            _target = target;
            _followDistance = followDistance;
        }

        public override BTNodeStatus Execute(Character self)
        {
            if (_target == null || !_target.IsAlive())
            {
                IsComplete = true;
                return BTNodeStatus.Failure;
            }

            // Si la cible est en combat et que nous ne le sommes pas, on la rejoint
            if (_target.CharacterCombat.IsInBattle && !self.CharacterCombat.IsInBattle)
            {
                self.CharacterCombat.JoinBattleAsAlly(_target);
                Debug.Log($"<color=green>[Order]</color> {self.CharacterName} rejoint {_target.CharacterName} au combat (défense) !");
                return BTNodeStatus.Running;
            }

            // Si on est en combat, on laisse le CombatBehaviour gérer
            if (self.CharacterCombat.IsInBattle)
            {
                return BTNodeStatus.Running;
            }

            // Sinon, suivre la cible
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            float dist = Vector3.Distance(self.transform.position, _target.transform.position);

            if (dist > _followDistance)
            {
                movement.SetDestination(_target.transform.position);
            }
            else if (dist <= _followDistance * 0.5f)
            {
                movement.Stop();
            }

            return BTNodeStatus.Running; // L'ordre ne se termine jamais seul
        }

        public override void Cancel(Character self)
        {
            base.Cancel(self);
            self.CharacterMovement?.ResetPath();
            Debug.Log($"<color=yellow>[Order]</color> Ordre de défense annulé pour {self.CharacterName}.");
        }

        public override bool IsValid()
        {
            return base.IsValid() && _target != null && _target.IsAlive();
        }
    }
}
