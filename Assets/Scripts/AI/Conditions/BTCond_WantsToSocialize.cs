using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Condition : le NPC veut socialiser avec un personnage détecté.
    /// Reproduit la logique sociale de NPCController (sociabilité, compatibilité).
    /// </summary>
    public class BTCond_WantsToSocialize : BTNode
    {
        private float _checkInterval = 10f;
        private float _lastCheckTime = -999f;
        private float _socialCooldown = 60f; // Cooldown après une interaction réussie
        private float _lastSocialTime = -999f;
        
        private Character _activeTarget;
        private bool _isMoving = false;

        protected override void OnExit(Blackboard bb)
        {
            if (_isMoving && bb.Self != null && bb.Self.CharacterMovement != null)
            {
                bb.Self.CharacterMovement.Stop();
            }
            _isMoving = false;
            _activeTarget = null;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsFree()) return ResetAndFail(self);

            // --- WORK FOCUS : NPCs at work don't initiate social interactions ---
            if (self.CharacterSchedule?.CurrentActivity == ScheduleActivity.Work) return ResetAndFail(self);

            // Si on est DÉJÀ en train de suivre quelqu'un:
            if (_activeTarget != null)
            {
                if (!_activeTarget.IsAlive() || !_activeTarget.IsFree()) return ResetAndFail(self);

                var movement = self.CharacterMovement;
                if (movement == null) return ResetAndFail(self);

                Vector3 targetPos = _activeTarget.transform.position;
                Vector3 currentPos = self.transform.position;
                currentPos.y = 0; targetPos.y = 0;
                
                float distance = Vector3.Distance(currentPos, targetPos);

                if (distance > 7f)
                {
                    if (!_isMoving || movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending))
                    {
                        movement.SetDestination(_activeTarget.transform.position);
                        _isMoving = true;
                    }
                    return BTNodeStatus.Running; // On continue d'avancer
                }

                if (_isMoving)
                {
                    movement.Stop();
                    _isMoving = false;
                }

                self.CharacterInteraction.StartInteractionWith(_activeTarget);
                _activeTarget = null;
                return BTNodeStatus.Success; // Terminé
            }

            // Cooldown après la dernière socialisation
            if (UnityEngine.Time.time - _lastSocialTime < _socialCooldown) return BTNodeStatus.Failure;

            // Déjà en interaction
            if (self.CharacterInteraction.IsInteracting) return BTNodeStatus.Failure;

            if (UnityEngine.Time.time - _lastCheckTime < _checkInterval) return BTNodeStatus.Failure;
            _lastCheckTime = UnityEngine.Time.time;

            // Vérifier la sociabilité via les traits
            if (self.CharacterTraits != null)
            {
                float sociability = self.CharacterTraits.GetSociability();
                if (Random.value > sociability) return BTNodeStatus.Failure;
            }

            var awareness = self.CharacterAwareness;
            if (awareness == null) return BTNodeStatus.Failure;

            var visibleCharacters = awareness.GetVisibleInteractables<CharacterInteractable>();
            var potentialTargets = visibleCharacters
                .Select(i => i.Character)
                .Where(c => c != null && c != self && c.IsAlive() && c.IsFree()
                    && !c.CharacterInteraction.IsInteracting
                    && c.CharacterSchedule?.CurrentActivity != ScheduleActivity.Work)
                .ToList();

            if (!potentialTargets.Any()) return BTNodeStatus.Failure;

            // Choisir la meilleure cible (priorité aux connaissances)
            Character target = potentialTargets
                .OrderByDescending(c =>
                {
                    float relScore = self.CharacterRelation?.GetRelationshipWith(c)?.RelationValue ?? 0f;
                    float distScore = -Vector3.Distance(self.transform.position, c.transform.position);
                    return relScore * 2f + distScore;
                })
                .FirstOrDefault();

            if (target == null) return BTNodeStatus.Failure;

            bb.Set(Blackboard.KEY_SOCIAL_TARGET, target);

            _lastSocialTime = UnityEngine.Time.time; // Cooldown démarre maintenant
            _activeTarget = target;

            Debug.Log($"<color=cyan>[BT Social]</color> {self.CharacterName} engage la conversation avec {target.CharacterName}.");
            return BTNodeStatus.Running; // Démarre le mouvement au prochain tick (ou à l'instant même selon le Selector)
        }

        private BTNodeStatus ResetAndFail(Character self)
        {
            if (_isMoving && self != null && self.CharacterMovement != null)
            {
                self.CharacterMovement.Stop();
            }
            _activeTarget = null;
            _isMoving = false;
            return BTNodeStatus.Failure;
        }
    }
}
