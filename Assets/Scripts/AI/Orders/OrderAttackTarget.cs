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

        public override NPCOrderType OrderType => NPCOrderType.AttackTarget;
        public Character Target => _target;

        public OrderAttackTarget(Character target)
        {
            _target = target;
        }

        public override BTNodeStatus Execute(Character self)
        {
            if (_target == null || !_target.IsAlive())
            {
                IsComplete = true;
                return BTNodeStatus.Success;
            }

            // Initier le combat si pas encore fait
            if (!_combatInitiated)
            {
                _combatInitiated = true;

                // On utilise le pattern existant : PushBehaviour → AttackTargetBehaviour
                NPCController npc = self.Controller as NPCController;
                if (npc != null)
                {
                    npc.PushBehaviour(new AttackTargetBehaviour(_target));
                }
                Debug.Log($"<color=red>[Order]</color> {self.CharacterName} attaque {_target.CharacterName} sur ordre !");
            }

            // L'ordre reste Running tant que le combat est actif
            if (self.CharacterCombat.IsInBattle)
            {
                return BTNodeStatus.Running;
            }

            // Le combat est terminé
            IsComplete = true;
            return BTNodeStatus.Success;
        }

        public override void Cancel(Character self)
        {
            base.Cancel(self);
            Debug.Log($"<color=yellow>[Order]</color> Ordre d'attaque annulé pour {self.CharacterName}.");
        }

        public override bool IsValid()
        {
            return base.IsValid() && _target != null && _target.IsAlive();
        }
    }
}
