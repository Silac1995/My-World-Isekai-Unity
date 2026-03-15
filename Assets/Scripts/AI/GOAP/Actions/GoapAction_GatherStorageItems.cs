using System.Collections.Generic;
using UnityEngine;
using System.Linq;

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

        // Valide s'il y a un objet au sol (WorldItem) dans la zone du bâtiment
        _targetItem = FindLooseWorldItem(worker);
        return _targetItem != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var movement = worker.CharacterMovement;
        if (movement == null) return;

        switch (_currentState)
        {
            case GatherState.FindingItem:
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

                Collider col = _targetItem.GetComponentInChildren<Collider>();
                if (col != null && !col.isTrigger)
                {
                    _targetPos = col.bounds.ClosestPoint(worker.transform.position);
                }
                else
                {
                    _targetPos = _targetItem.transform.position;
                }

                HandleMovementTo(worker, _targetPos, out bool arrivedAtItem);
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
                        // Fallback : détruire l'item au sol et porter manuellement
                        Object.Destroy(_targetItem.gameObject);
                        worker.CharacterVisual?.BodyPartsController?.HandsController?.CarryItem(itemInstance);
                        
                        DetermineStoragePosition();
                        _currentState = GatherState.MovingToStorage;
                        _actionStarted = false;
                    }
                }
                break;

            case GatherState.MovingToStorage:
                HandleMovementTo(worker, _targetPos, out bool arrivedAtStorage);
                if (arrivedAtStorage)
                {
                    _currentState = GatherState.DroppingOff;
                    _actionStarted = false;
                }
                break;

            case GatherState.DroppingOff:
                if (!_actionStarted)
                {
                    // L'item a été ramassé par CharacterPickUpItem (ou mis en main).
                    // On cherche l'item dans l'inventaire du worker.
                    ItemInstance carriedItem = GetCarriedItem(worker);
                    if (carriedItem == null)
                    {
                        _isComplete = true;
                        return;
                    }

                    var dropAction = new CharacterDropItem(worker, carriedItem);
                    if (worker.CharacterActions.ExecuteAction(dropAction))
                    {
                        _actionStarted = true;
                        dropAction.OnActionFinished += () => FinishDropoff(worker, carriedItem);
                    }
                    else
                    {
                        // Fallback destruction de l'équipement
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
        _targetPos = storageZone != null ? storageZone.GetRandomPointInZone() : _building.transform.position;
    }

    private void FinishDropoff(Character worker, ItemInstance item)
    {
        _building.AddToInventory(item);
        Debug.Log($"<color=green>[Gathering]</color> {worker.CharacterName} a rangé {item.ItemSO.ItemName} dans le stockage.");
        _currentState = GatherState.FindingItem;
        _actionStarted = false;
        
        // On vérifie s'il y a d'autres items, sinon on termine pour rendre la main
        if (FindLooseWorldItem(worker) == null)
        {
            _isComplete = true;
        }
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

    private void HandleMovementTo(Character worker, Vector3 targetPos, out bool arrived)
    {
        arrived = false;
        var movement = worker.CharacterMovement;

        float distance = Vector3.Distance(new Vector3(worker.transform.position.x, 0, worker.transform.position.z), new Vector3(targetPos.x, 0, targetPos.z));

        if (movement.PathPending) return;

        if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            if (distance > movement.StoppingDistance + 0.5f)
            {
                movement.SetDestination(targetPos);
            }
            else
            {
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

        Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);

        WorldItem nearest = null;
        float nearestDist = float.MaxValue;

        Zone storageZone = _building.StorageZone;
        BoxCollider storageCol = storageZone != null ? storageZone.GetComponent<BoxCollider>() : null;

        foreach (var col in colliders)
        {
            var worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (worldItem == null || worldItem.ItemInstance == null || worldItem.IsBeingCarried) continue;

            // Ignore les items qui sont déjà dans la zone de stockage (pour éviter un Gather infini)
            if (storageCol != null && storageCol.bounds.Contains(worldItem.transform.position)) continue;

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
