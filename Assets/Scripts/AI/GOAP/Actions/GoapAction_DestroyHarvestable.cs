using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GOAP action: navigate to a <see cref="Harvestable"/> claimed via a
/// <see cref="DestroyHarvestableTask"/>, run <see cref="CharacterAction_DestroyHarvestable"/>,
/// and let the spawned destruction outputs feed the existing PickupLooseItem chain. Mirrors
/// <see cref="GoapAction_HarvestResources"/>'s state machine but uses the destroy action and
/// completes after a single swing (destroying despawns the GameObject).
///
/// Cost is set HIGHER than the harvest action so the planner prefers harvesting whenever a
/// yield-path option exists. NPCs only fall through to destruction when:
///   1. The harvestable yields the wanted item ONLY via destruction (no yield path), AND
///   2. <see cref="Harvestable.AllowNpcDestruction"/> is true (designer opt-in), AND
///   3. <see cref="Harvestable.AllowDestruction"/> is true with the held tool matching
///      <see cref="Harvestable.RequiredDestructionTool"/> (or that tool is null = any).
/// </summary>
public class GoapAction_DestroyHarvestable : GoapAction
{
    public override string ActionName => "DestroyHarvestable";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasHarvestZone", true },
        { "looseItemExists", false },
        { "hasResources", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "looseItemExists", true }
    };

    // Higher than GoapAction_HarvestResources (1f) so the planner prefers harvest when both
    // are available. Destruction is irreversible / consumes the node, so it should only fire
    // when the planner has no harvest alternative.
    public override float Cost => 5f;

    private HarvestingBuilding _building;
    private bool _isComplete = false;
    private bool _isDestroying = false;
    private Harvestable _currentTarget = null;
    private CharacterAction_DestroyHarvestable _destroyAction = null;
    private DestroyHarvestableTask _assignedTask = null;

    public override bool IsComplete => _isComplete;

    public GoapAction_DestroyHarvestable(HarvestingBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        if (_building == null || !_building.HasHarvestableZone) return false;

        // Worker must be able to carry a wanted item afterwards (mirrors GoapAction_HarvestResources).
        var equipment = worker.CharacterEquipment;
        if (equipment != null)
        {
            var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
            bool handsFree = hands != null && hands.AreHandsFree();

            bool bagHasSpace = false;
            var wantedItems = _building.GetWantedItems();
            if (wantedItems != null && wantedItems.Count > 0)
            {
                foreach (var wantedItem in wantedItems)
                {
                    if (equipment.HasFreeSpaceForItemSO(wantedItem))
                    {
                        bagHasSpace = true;
                        break;
                    }
                }
            }
            else
            {
                bagHasSpace = equipment.HasFreeSpaceForMisc();
            }

            if (!handsFree && !bagHasSpace) return false;
        }

        // Symmetric to GoapAction_HarvestResources.IsValid — reject up front when no
        // claimable DestroyHarvestableTask exists for this worker. Without this guard the
        // planner could pick this action when only yield tasks exist (or vice-versa) and
        // loop on the empty-claim → _isComplete → replan path.
        var taskMgr = _building.TaskManager;
        if (taskMgr == null) return false;
        var wanted = _building.GetWantedItems();
        if (wanted == null || wanted.Count == 0) return false;
        return taskMgr.HasAvailableOrClaimedTask<DestroyHarvestableTask>(worker, task =>
        {
            var h = task.Target as Harvestable;
            if (h == null || worker.PathingMemory.IsBlacklisted(h.gameObject.GetInstanceID())) return false;
            if (!h.AllowDestruction || !h.AllowNpcDestruction) return false;
            return h.HasAnyDestructionOutput(wanted);
        });
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (!_building.HasHarvestableZone)
        {
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[GOAP Destroy]</color> {worker.CharacterName} : la zone de récolte a disparu !");
            _isComplete = true;
            return;
        }

        var movement = worker.CharacterMovement;
        if (movement == null) { _isComplete = true; return; }

        // Phase 1: claim a destroy task
        if (_currentTarget == null && _assignedTask == null)
        {
            // Quest-system auto-claim (CommercialBuilding.WorkerStartingShift → TryAutoClaimExistingQuests)
            // moves matching DestroyHarvestableTasks into _inProgressTasks before GOAP planning starts,
            // so check for a pre-existing claim FIRST. ClaimBestTask only walks _availableTasks; without
            // this fallback the worker's IsValid passes (HasAvailableOrClaimedTask sees the in-progress
            // claim) but Phase 1 returns null and we infinite-loop between Idle and DestroyHarvestable.
            System.Predicate<DestroyHarvestableTask> filter = task =>
            {
                var interactable = task.Target as Harvestable;
                if (interactable == null) return false;
                if (worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID())) return false;
                if (!interactable.AllowDestruction || !interactable.AllowNpcDestruction) return false;
                return interactable.HasAnyDestructionOutput(_building.GetWantedItems());
            };
            _assignedTask = _building.TaskManager?.FindClaimedTaskByWorker<DestroyHarvestableTask>(worker, filter);
            if (_assignedTask == null)
                _assignedTask = _building.TaskManager?.ClaimBestTask<DestroyHarvestableTask>(worker, filter);

            if (_assignedTask != null)
                _currentTarget = _assignedTask.Target as Harvestable;

            if (_currentTarget == null || _assignedTask == null)
            {
                if (NPCDebug.VerboseActions)
                    Debug.Log($"<color=orange>[GOAP Destroy]</color> {worker.CharacterName} : no claimable destroy task. Available={_building.TaskManager?.AvailableTasks.Count}, InProgress={_building.TaskManager?.InProgressTasks.Count}. Returning to Planner.");
                _isComplete = true;
                return;
            }

            Vector3 gatherPos = _currentTarget.transform.position;
            if (_currentTarget.InteractionZone != null)
                gatherPos = _currentTarget.InteractionZone.bounds.ClosestPoint(worker.transform.position);
            movement.SetDestination(gatherPos);
            return;
        }

        // Phase 2: launch CharacterAction_DestroyHarvestable on arrival
        if (!_isDestroying)
        {
            if (_currentTarget == null)
            {
                if (NPCDebug.VerboseActions)
                    Debug.Log($"<color=red>[GOAP Destroy]</color> {worker.CharacterName} : target disappeared during travel.");
                _building.TaskManager?.UnclaimTask(_assignedTask, worker);
                _assignedTask = null; _currentTarget = null; _isComplete = true;
                return;
            }

            if (!movement.PathPending)
            {
                bool isAtTarget = false;
                if (_currentTarget.InteractionZone != null)
                {
                    if (_currentTarget.InteractionZone.bounds.Contains(worker.transform.position))
                        isAtTarget = true;
                    else
                    {
                        // Tolerance must match (or be looser than) CharacterAction_DestroyHarvestable.CanExecute,
                        // which fails the action at dist > 2.5f. Previously 1.5f here, which made the GOAP
                        // give up before the action's own range check would have accepted the swing — NavMesh
                        // routinely stops the agent on the outside edge of small InteractionZone bounds
                        // (e.g. apple tree's 2×2×2 box) at ~2 units from the bounds centre. The mismatch
                        // produced a 3-strike pathing-memory blacklist within ~1 second, then a soft loop
                        // between IdleInBuilding and DestroyHarvestable on every hourly blacklist clear.
                        float dist = Vector3.Distance(worker.transform.position, _currentTarget.InteractionZone.bounds.ClosestPoint(worker.transform.position));
                        if (dist <= 2.5f) isAtTarget = true;
                    }
                }
                else
                {
                    isAtTarget = Vector3.Distance(worker.transform.position, _currentTarget.transform.position) <= 3f;
                }

                if (isAtTarget)
                {
                    worker.transform.LookAt(new Vector3(_currentTarget.transform.position.x, worker.transform.position.y, _currentTarget.transform.position.z));

                    _isDestroying = true;
                    _destroyAction = new CharacterAction_DestroyHarvestable(worker, _currentTarget);

                    _destroyAction.OnActionFinished += () =>
                    {
                        if (NPCDebug.VerboseActions)
                            Debug.Log($"<color=cyan>[GOAP Destroy]</color> {worker.CharacterName} finished destroying {_currentTarget?.name ?? "<null>"}.");
                        // Destruction always completes the task in one swing — the harvestable
                        // is despawned by CharacterAction_DestroyHarvestable.OnApplyEffect.
                        _building.TaskManager?.CompleteTask(_assignedTask);
                        _assignedTask = null;
                        _isComplete = true;
                    };

                    if (!worker.CharacterActions.ExecuteAction(_destroyAction))
                    {
                        if (NPCDebug.VerboseActions)
                            Debug.Log($"<color=orange>[GOAP Destroy]</color> {worker.CharacterName} cannot start destroy action (wrong tool / out of range).");
                        _building.TaskManager?.UnclaimTask(_assignedTask, worker);
                        _assignedTask = null; _isComplete = true;
                    }
                }
                else if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.2f)
                {
                    if (NPCDebug.VerboseActions)
                        Debug.Log($"<color=red>[GOAP Destroy]</color> {worker.CharacterName} blocked or out of reach to {_currentTarget.gameObject.name}.");
                    if (_currentTarget != null)
                        worker.PathingMemory.RecordFailure(_currentTarget.gameObject.GetInstanceID());
                    _building.TaskManager?.UnclaimTask(_assignedTask, worker);
                    _assignedTask = null; _currentTarget = null; _isComplete = true;
                }
            }
            return;
        }

        // Resume on interruption (combat etc.)
        if (_isDestroying && worker.CharacterActions.CurrentAction != _destroyAction)
        {
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=red>[GOAP Destroy]</color> {worker.CharacterName} : destroy interrupted. Resetting goal.");
            _isComplete = true;
        }
    }

    public override void Exit(Character worker)
    {
        if (_assignedTask != null)
        {
            _building.TaskManager?.UnclaimTask(_assignedTask, worker);
            _assignedTask = null;
        }
        _isDestroying = false;
        _currentTarget = null;
        worker.CharacterMovement?.ResetPath();
    }
}
