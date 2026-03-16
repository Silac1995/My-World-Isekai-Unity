using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Ramasser un objet au sol (WorldItem) dans la zone de récolte.
/// Se déclenche quand looseItemExists = true.
/// </summary>
public class GoapAction_PickupLooseItem : GoapAction
{
    public override string ActionName => "Pickup Loose Item";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "looseItemExists", true },
        { "hasResources", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasResources", true },
        { "looseItemExists", false }
    };

    public override float Cost => 0.5f;

    private GatheringBuilding _building;
    private bool _isComplete = false;
    private bool _pickupStarted = false;
    private WorldItem _targetWorldItem = null;
    private PickupLooseItemTask _assignedTask = null;

    public override bool IsComplete => _isComplete;

    public GoapAction_PickupLooseItem(GatheringBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        if (_building == null || !_building.HasGatherableZone) return false;

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

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        // 1. Trouver un objet au sol
        if (_targetWorldItem == null && _assignedTask == null)
        {
            _assignedTask = _building.TaskManager?.ClaimBestTask<PickupLooseItemTask>(worker, task => 
            {
                var interactable = task.Target as WorldItem;
                if (interactable == null) return true;
                return !worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID());
            });

            if (_assignedTask != null)
            {
                _targetWorldItem = _assignedTask.Target as WorldItem;
            }

            if (_targetWorldItem != null)
            {
                Debug.Log($"<color=cyan>[GOAP Pickup]</color> {worker.CharacterName} a vu {_targetWorldItem.ItemInstance.ItemSO.ItemName} par terre.");
                Vector3 targetPos = _targetWorldItem.transform.position;
                var itemInteractable = _targetWorldItem.ItemInteractable;
                if (itemInteractable != null && itemInteractable.InteractionZone != null)
                {
                    targetPos = itemInteractable.InteractionZone.bounds.ClosestPoint(worker.transform.position);
                }
                else
                {
                    Collider col = _targetWorldItem.GetComponentInChildren<Collider>();
                    if (col != null && !col.isTrigger)
                    {
                        targetPos = col.bounds.ClosestPoint(worker.transform.position);
                    }
                }
                
                movement.SetDestination(targetPos);
                return;
            }
            else
            {
                Debug.Log($"<color=orange>[GOAP Pickup]</color> {worker.CharacterName} : Plus d'items par terre ou tâche invalide.");
                _isComplete = true; // Il a peut-être été volé
                return;
            }
        }

        // 2. Marcher vers l'objet
        if (!movement.PathPending)
        {
            // NEW: Abort if object was destroyed mid-walk
            if (_targetWorldItem == null)
            {
                Debug.Log($"<color=red>[GOAP Pickup]</color> {worker.CharacterName} : La cible WorldItem a disparu pendant le trajet !");
                _building.TaskManager?.UnclaimTask(_assignedTask);
                _assignedTask = null;
                _targetWorldItem = null;
                _isComplete = true;
                return;
            }

            if (!movement.HasPath && movement.RemainingDistance > movement.StoppingDistance + 0.5f) 
            {
                Debug.Log($"<color=red>[GOAP Pickup]</color> {worker.CharacterName} : Impossible d'atteindre l'objet. Blacklist.");
                
                worker.PathingMemory.RecordFailure(_targetWorldItem.gameObject.GetInstanceID());
                
                // Le chemin a été effacé mais on n'est pas arrivé : on annule et on cherche de nouveau
                _building.TaskManager?.UnclaimTask(_assignedTask);
                _assignedTask = null;
                _targetWorldItem = null;
                return;
            }
            else
            {
                bool isAtWorldItem = false;
                var workerCol = worker.GetComponent<Collider>();
                var itemInteractable = _targetWorldItem.ItemInteractable;

                if (itemInteractable != null && itemInteractable.InteractionZone != null)
                {
                    if (itemInteractable.InteractionZone.bounds.Contains(worker.transform.position))
                    {
                        isAtWorldItem = true;
                    }
                    else
                    {
                        float dist = Vector3.Distance(worker.transform.position, itemInteractable.InteractionZone.bounds.ClosestPoint(worker.transform.position));
                        if (dist <= 0.5f)
                        {
                            isAtWorldItem = true;
                        }
                    }
                }
                else
                {
                    isAtWorldItem = movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
                }

                // Fallback structuré NavMesh (Règle le freeze à la bordure de l'interactionZone)
                if (!isAtWorldItem && movement.RemainingDistance <= movement.StoppingDistance + 0.75f)
                {
                    isAtWorldItem = true;
                }

                if (isAtWorldItem && !_pickupStarted)
                {
                    movement.Stop();
                    // Assurer qu'on regarde l'objet
                    worker.transform.LookAt(new Vector3(_targetWorldItem.transform.position.x, worker.transform.position.y, _targetWorldItem.transform.position.z));
                    
                    _pickupStarted = true;
                    PickupSpecificWorldItem(worker, _targetWorldItem);
                }
            }
        }

        // 3. Attendre la fin du pickup
        if (_pickupStarted)
        {
            if (worker.CharacterActions.CurrentAction == null || !(worker.CharacterActions.CurrentAction is CharacterPickUpItem))
            {
                if (!_isComplete) 
                {
                    Debug.Log($"<color=red>[GOAP Pickup]</color> {worker.CharacterName} : Ramassage interrompu !");
                    _building.TaskManager?.UnclaimTask(_assignedTask);
                    _assignedTask = null;
                    _isComplete = true;
                }
            }
        }
    }

    private void PickupSpecificWorldItem(Character worker, WorldItem worldItem)
    {
        ItemInstance itemInstance = worldItem.ItemInstance;
        if (itemInstance == null)
        {
            _isComplete = true;
            return;
        }

        var equipment = worker.CharacterEquipment;
        if (equipment != null && equipment.HaveInventory())
        {
            var inventory = equipment.GetInventory();
            if (inventory.HasFreeSpaceForItem(itemInstance))
            {
                var pickupAction = new CharacterPickUpItem(worker, itemInstance, worldItem.gameObject);
                pickupAction.OnActionFinished += () =>
                {
                    Debug.Log($"<color=green>[GOAP Pickup]</color> {worker.CharacterName} a mis {itemInstance.ItemSO.ItemName} dans son sac.");
                    _building.TaskManager?.CompleteTask(_assignedTask);
                    _assignedTask = null;
                    _isComplete = true;
                };

                if (!worker.CharacterActions.ExecuteAction(pickupAction))
                {
                    CarryItemFallback(worker, itemInstance, worldItem);
                }
                return;
            }
        }

        CarryItemFallback(worker, itemInstance, worldItem);
    }

    private void CarryItemFallback(Character worker, ItemInstance itemInstance, WorldItem worldItem)
    {
        var pickupAction = new CharacterPickUpItem(worker, itemInstance, worldItem.gameObject);
        if (worker.CharacterActions.ExecuteAction(pickupAction))
        {
            Debug.Log($"<color=green>[GOAP Pickup]</color> {worker.CharacterName} ramasse {itemInstance.ItemSO.ItemName} à la main.");
            pickupAction.OnActionFinished += () =>
            {
                _building.TaskManager?.CompleteTask(_assignedTask);
                _assignedTask = null;
                _isComplete = true;
            };
        }
        else
        {
            Debug.Log($"<color=orange>[GOAP Pickup]</color> {worker.CharacterName} ne peut ni stocker ni porter l'item.");
            _isComplete = true;
        }
    }

    public override void Exit(Character worker)
    {
        if (_assignedTask != null)
        {
            _building.TaskManager?.UnclaimTask(_assignedTask);
            _assignedTask = null;
        }
        
        _isComplete = false;
        _pickupStarted = false;
        _targetWorldItem = null;
        worker.CharacterMovement?.ResetPath();
    }
}
