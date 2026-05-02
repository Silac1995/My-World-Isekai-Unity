using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Se rendre à la zone de récolte et récolter un Harvestable.
/// Produit looseItemExists = true (grâce au spawn de l'item).
/// L'action Pickup prendra le relais.
/// </summary>
public class GoapAction_HarvestResources : GoapAction
{
    public override string ActionName => "HarvestResources";

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

    public override float Cost => 1f;

    private HarvestingBuilding _building;
    private bool _isComplete = false;
    private bool _isHarvesting = false;
    private Harvestable _currentTarget = null;
    private CharacterHarvestAction _harvestAction = null;
    private HarvestResourceTask _assignedTask = null;

    public override bool IsComplete => _isComplete;

    public GoapAction_HarvestResources(HarvestingBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        if (_building == null || !_building.HasHarvestableZone) return false;

        // Si le worker ne peut plus rien porter (ni sac ni main), l'action est invalide
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

        // Reject the action up front when no matching HarvestResourceTask is reachable for
        // this worker. Without this check, the planner picks Harvest (cost 1) over Destroy
        // (cost 5) whenever it's "valid", the action fails to claim anything in Execute,
        // _isComplete = true, planner replans, picks Harvest again — workers loop between
        // Idle (when stuckWaitingForTrees flips on) and HarvestResources without ever
        // running. Mirrors the symmetric guard in GoapAction_DestroyHarvestable.IsValid.
        var taskMgr = _building.TaskManager;
        if (taskMgr == null) return false;
        var wanted = _building.GetWantedItems();
        if (wanted == null || wanted.Count == 0) return false;

        bool found = taskMgr.HasAvailableOrClaimedTask<HarvestResourceTask>(worker, task =>
        {
            var h = task.Target as Harvestable;
            if (h == null) return false;
            if (worker.PathingMemory.IsBlacklisted(h.gameObject.GetInstanceID())) return false;
            return h.HasAnyYieldOutput(wanted);
        });

        // Throttled diagnostic: when worldState's hasUnfilledHarvestTask is true but the
        // predicate-filtered version returns false, surface WHY each HarvestResourceTask
        // failed (blacklisted, no yield match, or task target null/invalid). 1 Hz per worker.
        if (!found)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastIsValidDumpTime > 1f)
            {
                _lastIsValidDumpTime = now;
                int total = 0, blacklisted = 0, noYieldMatch = 0, taskInvalid = 0, nullTarget = 0;
                var av = taskMgr.AvailableTasks;
                for (int i = 0; i < av.Count; i++)
                {
                    if (!(av[i] is HarvestResourceTask hrt)) continue;
                    total++;
                    if (!hrt.IsValid()) { taskInvalid++; continue; }
                    var h = hrt.Target as Harvestable;
                    if (h == null) { nullTarget++; continue; }
                    if (worker.PathingMemory.IsBlacklisted(h.gameObject.GetInstanceID())) { blacklisted++; continue; }
                    if (!h.HasAnyYieldOutput(wanted)) { noYieldMatch++; continue; }
                }
                Debug.Log(
                    $"<color=red>[HarvestResources.IsValid]</color> {worker.CharacterName} REJECTED: " +
                    $"total HarvestResourceTask in Available={total}, taskInvalid={taskInvalid}, " +
                    $"nullTarget={nullTarget}, blacklisted={blacklisted}, noYieldMatch={noYieldMatch}. " +
                    $"wanted=[{string.Join(",", System.Linq.Enumerable.Select(wanted, w => w?.ItemName ?? "null"))}].");
            }
        }
        return found;
    }

    private float _lastIsValidDumpTime = -10f;

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (!_building.HasHarvestableZone)
        {
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[GOAP Harvest]</color> {worker.CharacterName} : la zone de récolte a disparu !");
            _isComplete = true;
            return;
        }

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        // Phase 1 : Trouver un objet à récolter (Arbre, etc.)
        if (_currentTarget == null && _assignedTask == null)
        {
            // Pre-existing claim from quest-system auto-claim (CommercialBuilding.WorkerStartingShift)
            // takes precedence — see GoapAction_DestroyHarvestable for the full reasoning.
            System.Predicate<HarvestResourceTask> filter = task =>
            {
                var interactable = task.Target as Harvestable;
                if (interactable == null || worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID()))
                    return false;
                return interactable.HasAnyOutput(_building.GetWantedItems());
            };
            _assignedTask = _building.TaskManager?.FindClaimedTaskByWorker<HarvestResourceTask>(worker, filter);
            if (_assignedTask == null)
                _assignedTask = _building.TaskManager?.ClaimBestTask<HarvestResourceTask>(worker, filter);

            if (_assignedTask != null)
            {
                _currentTarget = _assignedTask.Target as Harvestable;
            }

            if (_currentTarget == null || _assignedTask == null)
            {
                if (NPCDebug.VerboseActions)
                    Debug.Log($"<color=orange>[GOAP Harvest]</color> {worker.CharacterName} : No available tasks right now (maybe all are claimed). Returning to Planner.");
                _isComplete = true;
                return;
            }

            Vector3 gatherPos = _currentTarget.transform.position;
            if (_currentTarget.InteractionZone != null)
            {
                gatherPos = _currentTarget.InteractionZone.bounds.ClosestPoint(worker.transform.position);
            }
            movement.SetDestination(gatherPos);
            return;
        }

        // Phase 2 : Lancer la CharacterHarvestAction quand on est arrivé
        if (!_isHarvesting)
        {
            // NEW: Abort if object was destroyed mid-walk
            if (_currentTarget == null)
            {
                if (NPCDebug.VerboseActions)
                    Debug.Log($"<color=red>[GOAP Harvest]</color> {worker.CharacterName} : La cible Harvestable a disparu pendant le trajet !");
                _building.TaskManager?.UnclaimTask(_assignedTask, worker);
                _assignedTask = null;
                _currentTarget = null;
                _isComplete = true;
                return;
            }

            if (!movement.PathPending)
            {
                bool isAtTarget = false;

                if (_currentTarget.InteractionZone != null)
                {
                    if (_currentTarget.InteractionZone.bounds.Contains(worker.transform.position))
                    {
                        isAtTarget = true;
                    }
                    else
                    {
                        // Fallback distance must match the underlying CharacterHarvestAction range
                        // tolerance so GOAP and Action agree on "close enough". Was 1.5f, bumped to
                        // 2.5f after a tolerance-mismatch loop on small InteractionZone bounds —
                        // see GoapAction_DestroyHarvestable for the full reasoning.
                        float dist = Vector3.Distance(worker.transform.position, _currentTarget.InteractionZone.bounds.ClosestPoint(worker.transform.position));
                        if (dist <= 2.5f)
                        {
                            isAtTarget = true;
                        }
                    }
                }
                else
                {
                    isAtTarget = Vector3.Distance(worker.transform.position, _currentTarget.transform.position) <= 3f;
                }

                if (isAtTarget)
                {
                    // S'assurer de faire face à la cible
                    worker.transform.LookAt(new Vector3(_currentTarget.transform.position.x, worker.transform.position.y, _currentTarget.transform.position.z));
                    
                    _isHarvesting = true;
                    _harvestAction = new CharacterHarvestAction(worker, _currentTarget);

                    _harvestAction.OnActionFinished += () =>
                    {
                        if (NPCDebug.VerboseActions)
                            Debug.Log($"<color=cyan>[GOAP Harvest]</color> {worker.CharacterName} a fini de récolter.");
                        
                        // Si le component ne permet plus d'être récolté, on complète la tâche
                        if (_currentTarget != null && !_currentTarget.CanHarvest())
                        {
                            _building.TaskManager?.CompleteTask(_assignedTask);
                        }
                        else
                        {
                            _building.TaskManager?.UnclaimTask(_assignedTask, worker); // Remettre dans la file
                        }
                        
                        _assignedTask = null;
                        _isComplete = true;
                    };

                    if (!worker.CharacterActions.ExecuteAction(_harvestAction))
                    {
                        if (NPCDebug.VerboseActions)
                            Debug.Log($"<color=orange>[GOAP Harvest]</color> {worker.CharacterName} ne peut pas lancer la récolte.");
                        _building.TaskManager?.UnclaimTask(_assignedTask, worker);
                        _assignedTask = null;
                        _isComplete = true;
                    }
                }
                else if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.2f)
                {
                    // If we reached the end of the path but are still not in valid range, we are blocked physically
                    if (NPCDebug.VerboseActions)
                        Debug.Log($"<color=red>[GOAP Harvest]</color> {worker.CharacterName} est bloqué ou ne peut pas atteindre {_currentTarget.gameObject.name}. (Dist: {Vector3.Distance(worker.transform.position, _currentTarget.transform.position)})");
                    if (_currentTarget != null)
                    {
                        worker.PathingMemory.RecordFailure(_currentTarget.gameObject.GetInstanceID());
                    }
                    _building.TaskManager?.UnclaimTask(_assignedTask, worker);
                    _assignedTask = null;
                    _currentTarget = null;
                    _isComplete = true;
                }
            }
            return;
        }

        // Reprise sur interruption : L'action a été annulée (ex: par le combat)
        if (_isHarvesting)
        {
            if (worker.CharacterActions.CurrentAction != _harvestAction)
            {
                if (NPCDebug.VerboseActions)
                    Debug.Log($"<color=red>[GOAP Harvest]</color> {worker.CharacterName} : La récolte a été interrompue. Action annulée et on réinitialise l'objectif.");
                _isComplete = true;
            }
        }
    }

    public override void Exit(Character worker)
    {
        if (_assignedTask != null)
        {
            // On libère la tâche si elle n'a pas été complétée
            _building.TaskManager?.UnclaimTask(_assignedTask, worker);
            _assignedTask = null;
        }

        _isHarvesting = false;
        _currentTarget = null;
        worker.CharacterMovement?.ResetPath();
    }
}
