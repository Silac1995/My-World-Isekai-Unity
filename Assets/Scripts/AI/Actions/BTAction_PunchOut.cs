using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Action de forcer un NPC à dépointer d'un bâtiment commercial quand son horaire se termine.
    /// Se déplace vers la BuildingZone et déclenche Action_PunchOut.
    /// </summary>
    public class BTAction_PunchOut : BTNode
    {
        private enum PunchOutPhase
        {
            MovingToBuilding,
            PunchingOut
        }

        private PunchOutPhase _currentPhase = PunchOutPhase.MovingToBuilding;

        protected override void OnEnter(Blackboard bb)
        {
            _currentPhase = PunchOutPhase.MovingToBuilding;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            CommercialBuilding workplace = self.CharacterJob?.Workplace;
            if (workplace == null || !workplace.ActiveWorkersOnShift.Contains(self))
            {
                // Si on a plus de building ou qu'on n'est plus dedans (Action_PunchOut a réussi), succès.
                return BTNodeStatus.Success;
            }

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            switch (_currentPhase)
            {
                case PunchOutPhase.MovingToBuilding:
                    return HandleMovementToBuilding(self, movement, workplace);
                case PunchOutPhase.PunchingOut:
                    return HandlePunchingOut(self);
            }

            return BTNodeStatus.Failure;
        }

        private BTNodeStatus HandleMovementToBuilding(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            if (workplace.BuildingZone != null && workplace.BuildingZone.bounds.Contains(self.transform.position))
            {
                movement.Stop();
                _currentPhase = PunchOutPhase.PunchingOut;
                
                Action_PunchOut punchOut = new Action_PunchOut(self, workplace);
                if (punchOut.CanExecute())
                {
                    self.CharacterActions.ExecuteAction(punchOut);
                    return BTNodeStatus.Running;
                }
            }

            if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                if (workplace.BuildingZone != null)
                {
                    Vector3 dest = workplace.GetRandomPointInBuildingZone(self.transform.position.y);
                    movement.SetDestination(dest);
                }
            }
            
            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandlePunchingOut(Character self)
        {
            var currentAction = self.CharacterActions.CurrentAction;
            if (currentAction != null && currentAction is Action_PunchOut)
            {
                return BTNodeStatus.Running; // Toujours en train de lire l'animation
            }

            // L'action est terminée, on devrait avoir quitté
            return BTNodeStatus.Success;
        }

        protected override void OnExit(Blackboard bb)
        {
            base.OnExit(bb);
            bb.Self?.CharacterMovement?.ResetPath();
            
            // Sécurité FailSafe si le root nous abort avant la fin de l'anim
            Character self = bb.Self;
            if (self != null && self.CharacterJob != null)
            {
                var workplace = self.CharacterJob.Workplace;
                if (workplace != null && workplace.ActiveWorkersOnShift.Contains(self))
                {
                    workplace.WorkerEndingShift(self);
                }
            }

            // Réinitialiser le GOAP pour qu'il passe à autre chose (ex: rentrer chez soi, manger)
            if (self != null && self.CharacterGoap != null)
            {
                self.CharacterGoap.CancelPlan();
            }
        }
    }
}
