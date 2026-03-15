using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Ordre : attaquer une cible spécifique.
    /// Le NPC se déplace vers la cible et lance l'attaque.
    /// </summary>
    public class OrderAttackTarget : NPCOrder
    {
        private Character _target;
        private bool _combatInitiated = false;
        private BTAction_AttackTarget _attackNode;

        public override NPCOrderType OrderType => NPCOrderType.AttackTarget;
        public Character Target => _target;

        public OrderAttackTarget(Character target)
        {
            _target = target;
            _attackNode = new BTAction_AttackTarget();
        }

        public override BTNodeStatus Execute(Character self)
        {
            NPCController npc = self.Controller as NPCController;
            if (npc == null || npc.BehaviourTree == null) return BTNodeStatus.Failure;

            Blackboard bb = npc.BehaviourTree.Blackboard;

            if (_target == null || !_target.IsAlive())
            {
                IsComplete = true;
                bb.Remove(Blackboard.KEY_COMBAT_TARGET);
                return BTNodeStatus.Success;
            }

            // Initier le combat si pas encore fait
            if (!_combatInitiated)
            {
                _combatInitiated = true;
                bb.Set(Blackboard.KEY_COMBAT_TARGET, _target);
                Debug.Log($"<color=red>[Order]</color> {self.CharacterName} attaque {_target.CharacterName} sur ordre !");
            }

            // Exécution native de l'approche et du combat
            BTNodeStatus status = _attackNode.Execute(bb);

            if (status != BTNodeStatus.Running && !self.CharacterCombat.IsInBattle)
            {
                IsComplete = true;
                return BTNodeStatus.Success;
            }

            // L'ordre reste Running tant que le combat est actif ou l'approche est en cours
            return BTNodeStatus.Running;
        }

        public override void Cancel(Character self)
        {
            base.Cancel(self);
            NPCController npc = self.Controller as NPCController;
            if (npc != null && npc.BehaviourTree != null)
            {
                npc.BehaviourTree.Blackboard.Remove(Blackboard.KEY_COMBAT_TARGET);
            }
            Debug.Log($"<color=yellow>[Order]</color> Ordre d'attaque annulé pour {self.CharacterName}.");
        }

        public override bool IsValid()
        {
            return base.IsValid() && _target != null && _target.IsAlive();
        }
    }
}
