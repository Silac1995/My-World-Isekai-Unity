using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// New native BT node to handle the work cycle without the Legacy stack.
    /// Phase 1: Move to the building.
    /// Phase 1b: Move to the TimeClock and Interact (triggers Action_PunchIn).
    ///           If the building has no authored TimeClock, this phase is skipped and
    ///           falls back to the historical behaviour (direct Action_PunchIn in the
    ///           zone), with a one-shot warning.
    /// Phase 2: Wait for the Punch In animation.
    /// Phase 3: Execute the Job (Job.Execute()).
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

        // Per-job execute cadence (perf, see wiki/projects/optimisation-backlog.md entry #2 / Cₐ).
        // BT ticks at ~10 Hz; default Job.ExecuteIntervalSeconds is 0.1 s so default behaviour
        // is unchanged. Heavy-planning jobs (LogisticsManager, Harvester) override to a longer
        // interval. _lastExecuteTime is per-NPC because each NPC has its own BT instance.
        private float _lastExecuteTime = -1f;

        protected override void OnEnter(Blackboard bb)
        {
            _currentPhase = WorkPhase.MovingToBuilding;
            _warnedNoTimeClock = false;
            _warnedNoInteractable = false;
            _lastExecuteTime = -1f; // First HandleWorking call after entering Work always fires.
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

            // Already validated and registered by Action_PunchIn
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
                    return HandlePunchingIn(self, workplace);
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
                    if (!self.CharacterActions.ExecuteAction(punchIn))
                    {
                        Debug.LogWarning(
                            $"<color=orange>[BTAction_Work]</color> {self.CharacterName}: Action_PunchIn rejected at {workplace.BuildingName} (zone-punch fallback path). HandlePunchingIn will retry next tick.");
                    }
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
                    if (!self.CharacterActions.ExecuteAction(fallback))
                    {
                        Debug.LogWarning(
                            $"<color=orange>[BTAction_Work]</color> {self.CharacterName}: Action_PunchIn (no-interactable fallback) rejected at {workplace.BuildingName}. HandlePunchingIn will retry next tick.");
                    }
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

        private BTNodeStatus HandlePunchingIn(Character self, CommercialBuilding workplace)
        {
            var currentAction = self.CharacterActions.CurrentAction;
            if (currentAction != null && currentAction is Action_PunchIn)
            {
                return BTNodeStatus.Running;
            }

            // Action_PunchIn is no longer running. Two paths:
            // (a) It completed normally → OnApplyEffect → WorkerStartingShift → IsWorkerOnShift is
            //     true. Advance to Working so HandleWorking can call jobInfo.Work().
            // (b) It never ran (CharacterActions.ExecuteAction returned false because the worker was
            //     already busy with another action) OR was preempted before OnApplyEffect fired.
            //     Without this gate, the BT used to advance to Working anyway → JobFarmer.Execute
            //     ran → set _currentGoal to "PlantEmptyCells" → no _scratchValidActions → Idle
            //     forever, with the worker physically NOT on the shift roster. Symptom: debug shows
            //     "Job Goal: PlantEmptyCells, Action: Planning / Idle" but "On shift" doesn't include
            //     this worker. Falling back to MovingToTimeClock retries Interact + ExecuteAction.
            if (workplace.IsWorkerOnShift(self))
            {
                _currentPhase = WorkPhase.Working;
            }
            else
            {
                Debug.LogWarning(
                    $"<color=orange>[BTAction_Work]</color> {self.CharacterName}: PunchIn action ended without registering on shift at {workplace.BuildingName}. " +
                    $"Most likely CharacterActions.ExecuteAction was rejected (worker busy with another action). Retrying via MovingToTimeClock.");
                _currentPhase = WorkPhase.MovingToTimeClock;
            }
            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandleWorking(Character self, CharacterJob jobInfo)
        {
            // Per-job cadence: only call Job.Execute when the configured interval has elapsed.
            // The BT itself still ticks at ~10 Hz; this just throttles the heavy-planning job
            // logic (LogisticsManager, Harvester) without slowing the BT (combat reaction,
            // schedule transitions, etc.). See wiki/projects/optimisation-backlog.md
            // entry #2 / Cₐ. Action lifecycle (move/interact/etc) runs on its own per-frame
            // path inside CharacterMovement / CharacterActions and is NOT throttled here.
            var currentJob = jobInfo.CurrentJob;
            float interval = currentJob != null ? currentJob.ExecuteIntervalSeconds : 0.1f;
            // Fully qualified to avoid clash with the project's MWI.Time namespace.
            if (UnityEngine.Time.time - _lastExecuteTime >= interval)
            {
                jobInfo.Work();
                _lastExecuteTime = UnityEngine.Time.time;
            }
            return BTNodeStatus.Running; // This Node stays active as long as the Schedule hour is true.
        }

        protected override void OnExit(Blackboard bb)
        {
            base.OnExit(bb);
            bb.Self?.CharacterMovement?.ResetPath();
        }
    }
}
