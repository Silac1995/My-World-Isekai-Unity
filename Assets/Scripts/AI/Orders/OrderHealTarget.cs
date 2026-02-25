using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Ordre : soigner une cible.
    /// Le NPC s'approche de la cible et la soigne.
    /// TODO: Connecter au futur système de soin quand il sera implémenté.
    /// </summary>
    public class OrderHealTarget : NPCOrder
    {
        private Character _target;
        private float _healRange = 5f;
        private bool _hasHealed = false;

        public override NPCOrderType OrderType => NPCOrderType.HealTarget;
        public Character Target => _target;

        public OrderHealTarget(Character target)
        {
            _target = target;
        }

        public override BTNodeStatus Execute(Character self)
        {
            if (_target == null || !_target.IsAlive())
            {
                IsComplete = true;
                return BTNodeStatus.Failure;
            }

            // Vérifier si la cible a besoin de soin
            if (_target.Stats.Health.CurrentAmount >= _target.Stats.Health.CurrentValue)
            {
                IsComplete = true;
                Debug.Log($"<color=green>[Order]</color> {_target.CharacterName} n'a pas besoin de soin.");
                return BTNodeStatus.Success;
            }

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            float dist = Vector3.Distance(self.transform.position, _target.transform.position);

            // Se rapprocher de la cible
            if (dist > _healRange)
            {
                movement.SetDestination(_target.transform.position);
                return BTNodeStatus.Running;
            }

            // À portée : soigner
            if (!_hasHealed)
            {
                _hasHealed = true;
                movement.Stop();
                self.CharacterVisual?.FaceCharacter(_target);

                // TODO: Utiliser le vrai système de soin quand il sera implémenté
                // Pour l'instant, on fait un soin basique
                float healAmount = 30f;
                _target.Stats.Health.IncreaseCurrentAmount(healAmount);
                Debug.Log($"<color=green>[Order]</color> {self.CharacterName} soigne {_target.CharacterName} (+{healAmount} HP).");

                IsComplete = true;
                return BTNodeStatus.Success;
            }

            return BTNodeStatus.Running;
        }

        public override void Cancel(Character self)
        {
            base.Cancel(self);
            self.CharacterMovement?.ResetPath();
        }

        public override bool IsValid()
        {
            return base.IsValid() && _target != null && _target.IsAlive();
        }
    }
}
