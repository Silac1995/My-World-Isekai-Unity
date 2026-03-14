using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Nouveau noeud natif de BT pour gérer le cycle de travail sans la pile Legacy.
    /// Phase 1: Déplacement au bâtiment + PunchIn (Action).
    /// Phase 2: Exécuter le Job (Job.Execute()).
    /// </summary>
    public class BTAction_Work : BTNode
    {
        private enum WorkPhase
        {
            MovingToBuilding,
            PunchingIn,
            Working
        }

        private WorkPhase _currentPhase = WorkPhase.MovingToBuilding;

        protected override void OnEnter(Blackboard bb)
        {
            _currentPhase = WorkPhase.MovingToBuilding;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsFree(out CharacterBusyReason reason)) return BTNodeStatus.Failure;

            var jobInfo = self.CharacterJob;
            if (jobInfo == null || !jobInfo.HasJob) return BTNodeStatus.Failure;

            CommercialBuilding workplace = jobInfo.Workplace;
            if (workplace == null) return BTNodeStatus.Failure;

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            // Déjà validé et enregistré par Action_PunchIn
            if (workplace.ActiveWorkersOnShift.Contains(self))
            {
                _currentPhase = WorkPhase.Working;
            }

            switch (_currentPhase)
            {
                case WorkPhase.MovingToBuilding:
                    return HandleMovementToBuilding(self, movement, workplace);
                case WorkPhase.PunchingIn:
                    return HandlePunchingIn(self);
                case WorkPhase.Working:
                    return HandleWorking(self, jobInfo);
            }

            return BTNodeStatus.Failure;
        }

        private BTNodeStatus HandleMovementToBuilding(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            // Set Destination
            if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                if (workplace.BuildingZone != null && !workplace.BuildingZone.bounds.Contains(self.transform.position))
                {
                    Vector3 dest = workplace.GetRandomPointInBuildingZone(self.transform.position.y);
                    movement.SetDestination(dest);
                    return BTNodeStatus.Running;
                }
                else
                {
                    // Arrivé dans le bâtiment
                    movement.Stop();
                    _currentPhase = WorkPhase.PunchingIn;
                    
                    Action_PunchIn punchIn = new Action_PunchIn(self, workplace);
                    if (punchIn.CanExecute())
                    {
                        self.CharacterActions.ExecuteAction(punchIn);
                        return BTNodeStatus.Running;
                    }
                }
            }
            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandlePunchingIn(Character self)
        {
            var currentAction = self.CharacterActions.CurrentAction;
            if (currentAction != null && currentAction is Action_PunchIn)
            {
                return BTNodeStatus.Running;
            }

            // L'action est terminée, on devrait être dans ActiveWorkersOnShift
            _currentPhase = WorkPhase.Working;
            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandleWorking(Character self, CharacterJob jobInfo)
        {
            // C'est au job spécifique de s'occuper de son GOAP ou de ses states.
            jobInfo.Work();
            return BTNodeStatus.Running; // Ce Node reste actif tant que l'heure de Schedule est vraie.
        }

        protected override void OnExit(Blackboard bb)
        {
            base.OnExit(bb);
            bb.Self?.CharacterMovement?.ResetPath();
        }
    }
}
