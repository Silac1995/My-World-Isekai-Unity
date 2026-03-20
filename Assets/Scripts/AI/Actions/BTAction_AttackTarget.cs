using UnityEngine;
using UnityEngine.AI;

namespace MWI.AI
{
    /// <summary>
    /// Action de combat native pour le Behaviour Tree.
    /// Remplace "AttackTargetBehaviour". Gère le déplacement vers la cible
    /// et déclenche les attaques via CharacterCombat en tenant compte de la portée.
    /// </summary>
    public class BTAction_AttackTarget : BTNode
    {
        private Vector3 _lastTargetPos = Vector3.positiveInfinity;
        private float _lastRouteRequestTime = 0f;

        protected override void OnEnter(Blackboard bb)
        {
            _lastTargetPos = Vector3.positiveInfinity;
            _lastRouteRequestTime = 0f;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsAlive() || self.CharacterCombat == null)
            {
                return BTNodeStatus.Failure;
            }

            // Récupérer la cible depuis le Blackboard
            Character target = bb.Get<Character>(Blackboard.KEY_COMBAT_TARGET);
            
            // Si pas de cible ou cible morte/invalide, échec
            if (target == null || !target.IsAlive())
            {
                self.CharacterCombat.LeaveBattle();
                bb.Remove(Blackboard.KEY_COMBAT_TARGET);
                return BTNodeStatus.Failure;
            }

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            float distance = Vector3.Distance(self.transform.position, target.transform.position);

            // TODO: Remplacer par self.CharacterCombat.AttackRange quand ce sera implémenté
            float attackRange = 1.5f; 

            if (distance <= attackRange)
            {
                // A portée : arrêter de bouger et attaquer
                movement.Stop();
                self.CharacterVisual?.FaceCharacter(target);
                
                // Si le délai d'attaque est passé, on frappe
                if (self.CharacterCombat.IsReadyToAct)
                {
                    self.CharacterCombat.Attack(target);
                }
                
                bb.Set(Blackboard.KEY_COMBAT_TARGET, target);
                return BTNodeStatus.Running; // Toujours en combat
            }
            else
            {
                // Pas à portée : se rapprocher en évitant le spam NavMesh
                bool hasPathFailed = (UnityEngine.Time.unscaledTime - _lastRouteRequestTime > 0.2f) 
                                     && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid 
                                     || (!movement.HasPath && !movement.PathPending));

                if (Vector3.Distance(_lastTargetPos, target.transform.position) > 1f || hasPathFailed)
                {
                    movement.SetDestination(target.transform.position);
                    _lastTargetPos = target.transform.position;
                    _lastRouteRequestTime = UnityEngine.Time.unscaledTime;
                }
                
                bb.Set(Blackboard.KEY_COMBAT_TARGET, target);
                return BTNodeStatus.Running; // En approche
            }
        }

        protected override void OnExit(Blackboard bb)
        {
            bb.Self?.CharacterMovement?.Stop();
            bb.Remove(Blackboard.KEY_COMBAT_TARGET);
            _lastTargetPos = Vector3.positiveInfinity;
        }
    }
}
