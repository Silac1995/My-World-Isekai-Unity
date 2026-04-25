using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Nouveau noeud natif de BT pour gérer le cycle de travail sans la pile Legacy.
    /// Phase 1: Déplacement au bâtiment.
    /// Phase 1b: Déplacement au TimeClock + Interact (declenche Action_PunchIn).
    ///           Si le bâtiment n'a pas de TimeClock authored, saute ce phase et
    ///           retombe sur le comportement historique (Action_PunchIn direct en
    ///           zone), avec un warning one-shot.
    /// Phase 2: Attente animation Punch In.
    /// Phase 3: Exécuter le Job (Job.Execute()).
    /// </summary>
    public class BTAction_Work : BTNode
    {
        private enum WorkPhase
        {
            MovingToBuilding,
            MovingToTimeClock,
            PunchingIn,
            Working
        }

        private WorkPhase _currentPhase = WorkPhase.MovingToBuilding;
        private bool _warnedNoTimeClock = false;
        private bool _warnedNoInteractable = false;

        protected override void OnEnter(Blackboard bb)
        {
            _currentPhase = WorkPhase.MovingToBuilding;
            _warnedNoTimeClock = false;
            _warnedNoInteractable = false;
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
            if (workplace.IsWorkerOnShift(self))
            {
                _currentPhase = WorkPhase.Working;
            }

            switch (_currentPhase)
            {
                case WorkPhase.MovingToBuilding:
                    return HandleMovementToBuilding(self, movement, workplace);
                case WorkPhase.MovingToTimeClock:
                    return HandleMovementToTimeClock(self, movement, workplace);
                case WorkPhase.PunchingIn:
                    return HandlePunchingIn(self);
                case WorkPhase.Working:
                    return HandleWorking(self, jobInfo);
            }

            return BTNodeStatus.Failure;
        }

        private BTNodeStatus HandleMovementToBuilding(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            // Already inside BuildingZone? Advance to the TimeClock phase (which itself
            // soft-falls-back to zone-punch if no clock is authored).
            if (workplace.BuildingZone != null && workplace.BuildingZone.bounds.Contains(self.transform.position))
            {
                movement.Stop();
                _currentPhase = WorkPhase.MovingToTimeClock;
                return BTNodeStatus.Running;
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

        private BTNodeStatus HandleMovementToTimeClock(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            var clock = workplace.TimeClock;

            // Soft fallback (rule #4 — flag, don't silently skip): no clock authored.
            // Keep the legacy "punch anywhere in BuildingZone" path alive so existing
            // scenes without a clock still work until authoring catches up.
            if (clock == null)
            {
                if (!_warnedNoTimeClock)
                {
                    Debug.LogWarning($"<color=orange>[BTAction_Work]</color> {workplace.BuildingName} has no TimeClockFurniture. Falling back to zone-punch for {self.CharacterName}.");
                    _warnedNoTimeClock = true;
                }
                movement.Stop();
                _currentPhase = WorkPhase.PunchingIn;
                Action_PunchIn punchIn = new Action_PunchIn(self, workplace);
                if (punchIn.CanExecute())
                {
                    self.CharacterActions.ExecuteAction(punchIn);
                }
                return BTNodeStatus.Running;
            }

            var interactable = clock.GetComponent<TimeClockFurnitureInteractable>();
            if (interactable == null)
            {
                if (!_warnedNoInteractable)
                {
                    // One-shot per BT-branch-entry (OnEnter resets the flag). Otherwise this LogError
                    // fires every BT tick per NPC attempting to work at a misconfigured workplace and
                    // progressively fills the console buffer.
                    Debug.LogError($"<color=red>[BTAction_Work]</color> {workplace.BuildingName}'s TimeClockFurniture has no TimeClockFurnitureInteractable sibling. Falling back to zone-punch.");
                    _warnedNoInteractable = true;
                }
                movement.Stop();
                _currentPhase = WorkPhase.PunchingIn;
                Action_PunchIn fallback = new Action_PunchIn(self, workplace);
                if (fallback.CanExecute())
                {
                    self.CharacterActions.ExecuteAction(fallback);
                }
                return BTNodeStatus.Running;
            }

            // Arrival: canonical Interactable-System rule —
            // InteractableObject.IsCharacterInInteractionZone(character) tests
            // Character.transform.position against the authored InteractionZone
            // AABB. Zone size is the single source of truth; no bespoke distance
            // or social-zone overlap math.
            bool inZone = interactable.IsCharacterInInteractionZone(self);

            if (inZone)
            {
                movement.Stop();
                interactable.Interact(self);
                _currentPhase = WorkPhase.PunchingIn;
                return BTNodeStatus.Running;
            }

            // Not yet in range — path toward the authored InteractionPoint (standing
            // spot in front of the clock). Expect the author to place that point
            // inside the InteractionZone trigger bounds so the arrival check fires.
            Vector3 clockTarget = clock.GetInteractionPosition();
            if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                movement.SetDestination(clockTarget);
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
