using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Ordre : patrouiller dans une zone.
    /// Le NPC se dÃ©place entre des points alÃ©atoires dans la zone.
    /// Similaire au Wander mais contraint Ã  une zone spÃ©cifique.
    /// </summary>
    public class OrderPatrolZone : NPCOrder
    {
        private Vector3 _zoneCenter;
        private float _zoneRadius;
        private Vector3 _currentPatrolTarget;
        private bool _hasTarget = false;
        private float _waitTimer = 0f;
        private bool _isWaiting = false;

        public override NPCOrderType OrderType => NPCOrderType.PatrolZone;

        public OrderPatrolZone(Vector3 zoneCenter, float zoneRadius)
        {
            _zoneCenter = zoneCenter;
            _zoneRadius = zoneRadius;
        }

        public override BTNodeStatus Execute(Character self)
        {
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            // En attente entre les points de patrouille
            if (_isWaiting)
            {
                _waitTimer -= UnityEngine.Time.deltaTime;
                if (_waitTimer <= 0f)
                {
                    _isWaiting = false;
                    _hasTarget = false;
                }
                return BTNodeStatus.Running;
            }

            // Choisir un nouveau point de patrouille
            if (!_hasTarget)
            {
                Vector2 randomCircle = Random.insideUnitCircle * _zoneRadius;
                _currentPatrolTarget = _zoneCenter + new Vector3(randomCircle.x, 0, randomCircle.y);

                if (UnityEngine.AI.NavMesh.SamplePosition(_currentPatrolTarget, out UnityEngine.AI.NavMeshHit hit, _zoneRadius, UnityEngine.AI.NavMesh.AllAreas))
                {
                    _currentPatrolTarget = hit.position;
                    movement.SetDestination(_currentPatrolTarget);
                    _hasTarget = true;
                }
                return BTNodeStatus.Running;
            }

            // VÃ©rifier si on est arrivÃ©
            if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
            {
                _isWaiting = true;
                _waitTimer = Random.Range(2f, 5f);
            }

            return BTNodeStatus.Running; // La patrouille ne se termine jamais seule
        }

        public override void Cancel(Character self)
        {
            base.Cancel(self);
            self.CharacterMovement?.ResetPath();
            Debug.Log($"<color=yellow>[Order]</color> Ordre de patrouille annulÃ© pour {self.CharacterName}.");
        }
    }
}
