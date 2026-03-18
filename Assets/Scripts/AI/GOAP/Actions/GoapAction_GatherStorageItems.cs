using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using MWI.AI;

/// <summary>
/// Actions GOAP pour le LogisticsManager : Ramasse les WorldItems traînant dans le bâtiment
/// (produits par les artisans) et les range dans l'inventaire du bâtiment via StorageZone.
/// </summary>
public class GoapAction_GatherStorageItems : GoapAction
{
    public override string ActionName => "GatherStorageItems";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isIdling", true } // Permet de satisfaire le but Idle tout en effectuant une tâche de ménage
    };

    public override float Cost => 0.5f; // Moins cher que l'Idle global pour le forcer à ramasser d'abord

    private JobLogisticsManager _manager;
    private CommercialBuilding _building;
    private WorldItem _targetItem;
    private bool _isComplete = false;
    private bool _actionStarted = false;
    private GatherState _currentState = GatherState.FindingItem;
    private Vector3 _targetPos;

    private enum GatherState
    {
        FindingItem,
        MovingToItem,
        PickingUp,
        MovingToStorage,
        DroppingOff
    }

    public GoapAction_GatherStorageItems(JobLogisticsManager manager)
    {
        _manager = manager;
        _building = manager.Workplace as CommercialBuilding;
    }

    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        if (_isComplete || _building == null || _building.BuildingZone == null) return false;

        // Protection forte : si l'action physique de ramassage ou dépôt a commencé, 
        // on maintient la GOAP Action valide pour lui laisser le temps de finir.
        if (_actionStarted)
        {
            return true;
        }

        bool isCarrying = GetCarriedItem(worker) != null;

        var bManager = _building?.LogisticsManager;

        // Si on a des commandes en attente ET qu'on n'est pas déjà en train de transporter un objet, on invalide
        // l'action pour forcer la GOAP à l'annuler et repasser sur GoapAction_PlaceOrder.
        if (!isCarrying && bManager != null && bManager.HasPendingOrders)
        {
            return false;
        }

        // Valide s'il y a un objet au sol (WorldItem) dans la zone du bâtiment, OU si on porte déjà qqchose
        _targetItem = FindLooseWorldItem(worker);
        return _targetItem != null || isCarrying;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var movement = worker.CharacterMovement;
        if (movement == null) return;

        switch (_currentState)
        {
            case GatherState.FindingItem:
                bool isCarrying = GetCarriedItem(worker) != null;
                
                if (isCarrying)
                {
                    DetermineStoragePosition();
                    _currentState = GatherState.MovingToStorage;
                    _actionStarted = false;
                    return;
                }

                var bManager = _building?.LogisticsManager;
                // If we are empty-handed AND we have a pending order, we should stop gathering and allow re-planning (so we prioritize PlaceOrder).
                if (bManager != null && bManager.HasPendingOrders)
                {
                    _isComplete = true; // Prioritize PlaceOrder!
                    return;
                }

                if (_targetItem == null) _targetItem = FindLooseWorldItem(worker);
                
                if (_targetItem != null)
                {
                    _currentState = GatherState.MovingToItem;
                }
                else
                {
                    _isComplete = true; // Plus rien à ramasser
                }
                break;

            case GatherState.MovingToItem:
                if (_targetItem == null || _targetItem.gameObject == null)
                {
                    _currentState = GatherState.FindingItem;
                    return;
                }

                Collider targetCol = _targetItem.ItemInteractable?.InteractionZone;
                if (targetCol == null)
                {
                    targetCol = _targetItem.GetComponentInChildren<Collider>();
                }

                var interactable = _targetItem.GetComponentInChildren<InteractableObject>();
                if (interactable != null && interactable.InteractionZone != null)
                {
                    targetCol = interactable.InteractionZone;
                    _targetPos = targetCol.bounds.ClosestPoint(worker.transform.position);
                }
                else if (targetCol != null && !targetCol.isTrigger)
                {
                    _targetPos = targetCol.bounds.ClosestPoint(worker.transform.position);
                }
                else
                {
                    _targetPos = _targetItem.transform.position;
                }

                HandleMovementTo(worker, _targetPos, out bool arrivedAtItem, targetCol);
                if (arrivedAtItem)
                {
                    _currentState = GatherState.PickingUp;
                    _actionStarted = false;
                }
                break;

            case GatherState.PickingUp:
                if (_targetItem == null || _targetItem.gameObject == null)
                {
                    _currentState = GatherState.FindingItem;
                    return;
                }

                if (!_actionStarted)
                {
                    var itemInstance = _targetItem.ItemInstance;
                    var pickupAction = new CharacterPickUpItem(worker, itemInstance, _targetItem.gameObject);
                    
                    if (worker.CharacterActions.ExecuteAction(pickupAction))
                    {
                        _actionStarted = true;
                        pickupAction.OnActionFinished += () => 
                        {
                            DetermineStoragePosition();
                            _currentState = GatherState.MovingToStorage;
                            _actionStarted = false;
                        };
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[GOAP Storage]</color> {worker.CharacterName} n'a pas pu ramasser l'item.");
                        _currentState = GatherState.FindingItem;
                    }
                }
                break;

            case GatherState.MovingToStorage:
                Zone storageZone = _building.StorageZone != null ? _building.StorageZone : _building.MainRoom.GetComponent<Zone>();
                Collider storageCol = storageZone != null ? storageZone.GetComponent<Collider>() : null;

                HandleMovementTo(worker, _targetPos, out bool arrivedAtStorage, storageCol, true);

                // [FIX]: Permit an early delivery if the character is physically inside the storage zone, 
                // avoiding agents infinitely pushing each other at the exact _targetPos coordinate.
                if (!arrivedAtStorage && storageCol != null)
                {
                    Vector3 flatWorkerPos = new Vector3(worker.transform.position.x, storageCol.bounds.center.y, worker.transform.position.z);
                    Bounds safeBounds = storageCol.bounds;
                    
                    // We shrink the bounds by exactly the maximum drop offset (0.3 per side, so 0.6 total)
                    // to guarantee the physical item won't fall outside the zone and trigger an infinite gather loop.
                    safeBounds.Expand(-0.6f);
                    
                    if (safeBounds.Contains(flatWorkerPos))
                    {
                        worker.CharacterMovement.ResetPath();
                        arrivedAtStorage = true;
                    }
                }

                if (arrivedAtStorage)
                {
                    _currentState = GatherState.DroppingOff;
                    _actionStarted = false;
                }
                break;

            case GatherState.DroppingOff:
                if (!_actionStarted)
                {
                    // Cherche l'item dans l'inventaire du worker.
                    ItemInstance carriedItem = GetCarriedItem(worker);
                    if (carriedItem == null)
                    {
                        // Plus rien en main ou dans le sac, on a fini la livraison physique. 
                        // On retourne chercher s'il y a d'autres choses à ramasser.
                        _currentState = GatherState.FindingItem;
                        return;
                    }

                    var dropAction = new CharacterDropItem(worker, carriedItem, true);
                    
                    // Si true: L'action a été acceptée et l'animation commence.
                    if (worker.CharacterActions.ExecuteAction(dropAction))
                    {
                        _actionStarted = true;
                        dropAction.OnActionFinished += () => FinishDropoff(worker, carriedItem);
                    }
                    else
                    {
                        // L'action a été refusée (presque toujours parce qu'une action est DEJA en cours).
                        // S'il est bloqué avec une action fantôme, purger et forcer la livraison.
                        if (worker.CharacterActions.CurrentAction != null)
                        {
                            worker.CharacterActions.ClearCurrentAction();
                        }
                        RemoveItemFromWorker(worker, carriedItem);
                        FinishDropoff(worker, carriedItem);
                    }
                }
                break;
        }
    }

    private void DetermineStoragePosition()
    {
        Zone storageZone = _building.StorageZone ?? _building.MainRoom.GetComponent<Zone>();
        if (storageZone != null)
        {
            var bounds = storageZone.GetComponent<Collider>().bounds;
            Vector3 center = bounds.center;
            // Spread the targets dynamically within the inner 50% of the storage zone
            float varX = Mathf.Max(bounds.extents.x * 0.5f, 0.1f);
            float varZ = Mathf.Max(bounds.extents.z * 0.5f, 0.1f);
            _targetPos = center + new Vector3(Random.Range(-varX, varX), 0, Random.Range(-varZ, varZ));
        }
        else
        {
            _targetPos = _building.transform.position;
        }
    }

    private void FinishDropoff(Character worker, ItemInstance item)
    {
        _building.AddToInventory(item);
        Debug.Log($"<color=green>[Gathering]</color> {worker.CharacterName} a rangé {item.ItemSO.ItemName} dans le stockage.");
        
        var bManager = _building?.LogisticsManager;
        if (bManager != null)
        {
            bManager.OnItemGathered(item.ItemSO);
        }

        // On libère le verrou d'action
        _actionStarted = false;
        
        // On reste dans l'état DroppingOff ! 
        // Au prochain frame, le switch passera dans DroppingOff et appellera GetCarriedItem().
        // S'il reste des objets dans le sac, ça relancera une action de drop !
        // Si le sac est vide, le GetCarriedItem renverra null, et renverra vers FindingItem.
    }

    private ItemInstance GetCarriedItem(Character worker)
    {
        var inventory = worker.CharacterEquipment?.GetInventory();
        if (inventory != null && inventory.ItemSlots.Exists(s => !s.IsEmpty()))
        {
            return inventory.ItemSlots.FindLast(s => !s.IsEmpty()).ItemInstance;
        }
        return worker.CharacterVisual?.BodyPartsController?.HandsController?.CarriedItem;
    }

    private void RemoveItemFromWorker(Character worker, ItemInstance item)
    {
        var inventory = worker.CharacterEquipment?.GetInventory();
        if (inventory != null && inventory.HasAnyItemSO(new List<ItemSO> { item.ItemSO }))
        {
            inventory.RemoveItem(item, worker);
        }
        else
        {
            worker.CharacterVisual?.BodyPartsController?.HandsController?.DropCarriedItem();
        }
    }

    private void HandleMovementTo(Character worker, Vector3 targetPos, out bool arrived, Collider targetCollider = null, bool bypassEarlyExit = false)
    {
        arrived = false;
        var movement = worker.CharacterMovement;

        if (!bypassEarlyExit && NavMeshUtility.IsCharacterAtTargetZone(worker, targetCollider, 1.5f))
        {
            movement.ResetPath();
            arrived = true;
            return;
        }

        if (movement.PathPending) return;

        bool hasPathFailed = NavMeshUtility.HasPathFailed(movement, 0, 0.2f); // Using 0 for request time since it's not strictly tracked here, but usually HasPath=false is caught.
        
        // Custom Failure tracking
        if (!movement.HasPath || hasPathFailed)
        {
            if (targetCollider != null)
            {
                bool blacklisted = worker.PathingMemory.RecordFailure(targetCollider.gameObject.GetInstanceID());
                if (blacklisted)
                {
                    movement.Stop();
                    movement.ResetPath();
                    arrived = false;
                    _currentState = GatherState.FindingItem; // Abort and try to find another item
                    return;
                }
            }
        }

        // If we don't have a path, we definitely haven't started moving. Start now.
        if (!movement.HasPath)
        {
            movement.SetDestination(targetPos);
            return;
        }

        // We have a path. Let's check if we reached the end of it.
        if (movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            float distance = Vector3.Distance(new Vector3(worker.transform.position.x, 0, worker.transform.position.z), new Vector3(targetPos.x, 0, targetPos.z));

            if (targetCollider == null && distance > movement.StoppingDistance + 0.5f)
            {
                // We reached the end of the computed NavMesh path, but we are still far physically from the raw coordinate.
                // Could be blocked. Force a retry or push closer.
                movement.SetDestination(targetPos);
            }
            else
            {
                // We reached the end, and either we are close to the coordinate, OR we had a targetCollider (like an interaction zone) 
                // but couldn't path inside it. Just yield to the interaction phase so it can fail cleanly if unreachable.
                movement.ResetPath();
                arrived = true;
            }
        }
    }

    private WorldItem FindLooseWorldItem(Character worker)
    {
        if (_building.BuildingZone == null) return null;
        
        BoxCollider boxCol = _building.BuildingZone.GetComponent<BoxCollider>();
        if (boxCol == null) return null;

        Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
        Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

        Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);

        WorldItem nearest = null;
        float nearestDist = float.MaxValue;

        Zone storageZone = _building.StorageZone;
        BoxCollider storageCol = storageZone != null ? storageZone.GetComponent<BoxCollider>() : null;
        
        Zone depositZone = null;
        if (_building is GatheringBuilding gatheringBuilding)
        {
            depositZone = gatheringBuilding.DepositZone;
        }
        BoxCollider depositCol = depositZone != null ? depositZone.GetComponent<BoxCollider>() : null;
        
        Zone deliveryZone = _building.DeliveryZone;
        BoxCollider deliveryCol = deliveryZone != null ? deliveryZone.GetComponent<BoxCollider>() : null;

        List<Collider> allCols = new List<Collider>(colliders);

        if (depositCol != null)
        {
            Vector3 dCenter = depositCol.transform.TransformPoint(depositCol.center);
            Vector3 dHalfExtents = Vector3.Scale(depositCol.size, depositCol.transform.lossyScale) * 0.5f;
            var dCols = Physics.OverlapBox(dCenter, dHalfExtents, depositCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
            allCols.AddRange(dCols);
        }
        
        if (deliveryCol != null)
        {
            Vector3 delCenter = deliveryCol.transform.TransformPoint(deliveryCol.center);
            Vector3 delHalfExtents = Vector3.Scale(deliveryCol.size, deliveryCol.transform.lossyScale) * 0.5f;
            var delCols = Physics.OverlapBox(delCenter, delHalfExtents, deliveryCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
            allCols.AddRange(delCols);
        }

        foreach (var col in allCols)
        {
            var worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (worldItem == null || worldItem.ItemInstance == null || worldItem.IsBeingCarried) continue;

            if (worker.PathingMemory.IsBlacklisted(worldItem.gameObject.GetInstanceID())) continue;

            // Ignore les items qui sont déjà dans la zone de stockage (pour éviter un Gather infini)
            if (storageCol != null)
            {
                // Vérification avec Y aplati pour inclure les objets en train de tomber ou lévitant légèrement
                Vector3 flatPos = new Vector3(worldItem.transform.position.x, storageCol.bounds.center.y, worldItem.transform.position.z);
                if (storageCol.bounds.Contains(flatPos)) continue;
            }

            // Optional: check if it belongs to crafting output. 
            // Currently, any loose item in building zone gets stashed.
            float dist = Vector3.Distance(worker.transform.position, worldItem.transform.position);
            if (dist < nearestDist)
            {
                nearest = worldItem;
                nearestDist = dist;
            }
        }
        return nearest;
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _actionStarted = false;
        _currentState = GatherState.FindingItem;
        _targetItem = null;
    }
}
