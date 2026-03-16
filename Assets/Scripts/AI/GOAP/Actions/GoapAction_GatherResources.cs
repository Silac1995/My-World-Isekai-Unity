using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Se rendre à la zone de récolte et récolter un GatherableObject.
/// Produit looseItemExists = true (grâce au spawn de l'item).
/// L'action Pickup prendra le relais.
/// </summary>
public class GoapAction_GatherResources : GoapAction
{
    public override string ActionName => "GatherResources";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasGatherZone", true },
        { "looseItemExists", false },
        { "hasResources", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "looseItemExists", true }
    };

    public override float Cost => 1f;

    private GatheringBuilding _building;
    private bool _isComplete = false;
    private bool _isMovingToZone = false;
    private bool _arrivedAtZone = false;
    private bool _isGathering = false;
    private GatherableObject _currentTarget = null;
    private CharacterGatherAction _gatherAction = null;
    private GatherResourceTask _assignedTask = null;

    public override bool IsComplete => _isComplete;

    public GoapAction_GatherResources(GatheringBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        if (_building == null || !_building.HasGatherableZone) return false;

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

        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (!_building.HasGatherableZone)
        {
            Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : la zone de récolte a disparu !");
            _isComplete = true;
            return;
        }

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        // Phase 1 : Se déplacer vers la zone de récolte
        if (!_arrivedAtZone)
        {
            MoveToGatherZone(worker, movement);
            return;
        }

        // Phase 2 : Trouver un objet à récolter (Arbre, etc.)
        if (_currentTarget == null && _assignedTask == null)
        {
            _assignedTask = _building.TaskManager?.ClaimBestTask<GatherResourceTask>(worker, task => 
            {
                var interactable = task.Target as GatherableObject;
                if (interactable == null) return true;
                return !worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID());
            });
            
            if (_assignedTask != null)
            {
                _currentTarget = _assignedTask.Target as GatherableObject;
            }

            if (_currentTarget == null || _assignedTask == null)
            {
                Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : No available tasks right now (maybe all are claimed). Returning to Planner.");
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

        // Phase 3 : Lancer la CharacterGatherAction quand on est arrivé
        if (!_isGathering && _currentTarget != null)
        {
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
                        // Fallback : On vérifie si on est assez proche du centre (1.5f)
                        float dist = Vector3.Distance(worker.transform.position, _currentTarget.InteractionZone.bounds.ClosestPoint(worker.transform.position));
                        if (dist <= 1.5f)
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
                    
                    _isGathering = true;
                    _gatherAction = new CharacterGatherAction(worker, _currentTarget);

                    _gatherAction.OnActionFinished += () =>
                    {
                        Debug.Log($"<color=cyan>[GOAP Gather]</color> {worker.CharacterName} a fini de récolter.");
                        
                        // Si le component ne permet plus d'être récolté, on complète la tâche
                        if (_currentTarget != null && !_currentTarget.CanGather())
                        {
                            _building.TaskManager?.CompleteTask(_assignedTask);
                        }
                        else
                        {
                            _building.TaskManager?.UnclaimTask(_assignedTask); // Remettre dans la file
                        }
                        
                        _isComplete = true;
                    };

                    if (!worker.CharacterActions.ExecuteAction(_gatherAction))
                    {
                        Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} ne peut pas lancer la récolte.");
                        _building.TaskManager?.UnclaimTask(_assignedTask);
                        _isComplete = true;
                    }
                }
                else if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.2f)
                {
                    // If we reached the end of the path but are still not in valid range, we are blocked physically
                    Debug.Log($"<color=red>[GOAP Gather]</color> {worker.CharacterName} est bloqué ou ne peut pas atteindre {_currentTarget.gameObject.name}. (Dist: {Vector3.Distance(worker.transform.position, _currentTarget.transform.position)})");
                    if (_currentTarget != null)
                    {
                        worker.PathingMemory.RecordFailure(_currentTarget.gameObject.GetInstanceID());
                    }
                    _building.TaskManager?.UnclaimTask(_assignedTask);
                    _assignedTask = null;
                    _currentTarget = null;
                    _isComplete = true;
                }
            }
            return;
        }

        // Reprise sur interruption : L'action a été annulée (ex: par le combat)
        if (_isGathering)
        {
            if (worker.CharacterActions.CurrentAction != _gatherAction)
            {
                Debug.Log($"<color=red>[GOAP Gather]</color> {worker.CharacterName} : La récolte a été interrompue. Action annulée et on réinitialise l'objectif.");
                _isComplete = true; 
            }
        }
    }

    private void MoveToGatherZone(Character worker, CharacterMovement movement)
    {
        if (!_isMovingToZone)
        {
            // NEW CHECK : Si on est DÉJÀ dans la zone, on skip la marche d'entrée
            BoxCollider box = _building.GatherableZone.GetComponent<BoxCollider>();
            if (box != null && box.bounds.Contains(worker.transform.position))
            {
                _arrivedAtZone = true;
                return;
            }

            Vector3 destination = _building.GatherableZone.GetRandomPointInZone();
            movement.SetDestination(destination);
            _isMovingToZone = true;
            return;
        }

        if (!movement.PathPending)
        {
            if (!movement.HasPath)
            {
                _isMovingToZone = false;
            }
            else if (movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                _arrivedAtZone = true;
                _isMovingToZone = false;
                Debug.Log($"<color=cyan>[GOAP Gather]</color> {worker.CharacterName} est arrivé à la zone de récolte.");
            }
        }
    }

    public override void Exit(Character worker)
    {
        if (_assignedTask != null && !_isComplete)
        {
            // On libère la tâche si elle n'a pas été complétée
            _building.TaskManager?.UnclaimTask(_assignedTask);
            _assignedTask = null;
        }

        _isMovingToZone = false;
        _arrivedAtZone = false;
        _isGathering = false;
        _currentTarget = null;
        worker.CharacterMovement?.ResetPath();
    }
}
