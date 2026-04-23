using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.AI;

/// <summary>
/// GOAP action owned by <see cref="JobLogisticsManager"/>. When the building has
/// an authored <see cref="CommercialBuilding.PickupZone"/> AND a live
/// <see cref="TransportOrder"/> whose <see cref="TransportOrder.ReservedItems"/>
/// are still physically sitting in <see cref="CommercialBuilding.StorageZone"/>,
/// the logistics worker picks one reserved instance up and drops it inside the
/// PickupZone so the incoming transporter never has to navigate into storage.
///
/// Single-responsibility note: this is deliberately NOT an extension to
/// <c>GoapAction_GatherStorageItems</c>. That action moves loose stuff INTO
/// storage (inbound); this one moves reserved stuff OUT to the staging zone
/// (outbound). Same physical verbs (move, pickup, drop) — opposite direction +
/// opposite trigger condition. Keeping them separate avoids the monolith
/// <c>bool isOutbound</c> sprawl that always decays into untestable branching.
///
/// Cost: 0.2f so the planner prefers staging over GatherStorageItems (0.5f) but
/// still defers to the absolute-priority <c>GoapAction_PlaceOrder</c> (0.1f) when
/// outgoing orders haven't been placed yet.
/// </summary>
public class GoapAction_StageItemForPickup : GoapAction
{
    public override string ActionName => "StageItemForPickup";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isIdling", true } // satisfies the Idle goal so the planner considers this when nothing else is urgent
    };

    // Cheaper than GatherStorageItems (0.5f) so when both are valid the planner picks staging first.
    public override float Cost => 0.2f;

    private readonly JobLogisticsManager _manager;
    private readonly CommercialBuilding _building;

    private WorldItem _targetItem;
    private ItemInstance _targetInstance;
    private bool _isComplete;
    private bool _actionStarted;
    private StageState _state = StageState.Finding;
    private Vector3 _targetPos;

    private enum StageState
    {
        Finding,
        MovingToItem,
        PickingUp,
        MovingToPickup,
        DroppingOff
    }

    public GoapAction_StageItemForPickup(JobLogisticsManager manager)
    {
        _manager = manager;
        _building = manager?.Workplace;
    }

    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_building == null || _building.PickupZone == null || _building.StorageZone == null) return false;

        // Protection: once we physically started interacting, ride it out.
        if (_actionStarted) return true;

        // We also stay valid while already holding a reserved instance, regardless of any
        // book-keeping change mid-flight.
        if (_targetInstance != null && GetCarriedItem(worker) == _targetInstance) return true;

        // Is there anything worth staging? i.e. a reserved physical instance currently in StorageZone
        // that hasn't already been moved into PickupZone.
        _targetItem = FindReservedItemInStorage();
        return _targetItem != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        var movement = worker.CharacterMovement;
        if (movement == null) return;

        switch (_state)
        {
            case StageState.Finding:
                if (_targetItem == null) _targetItem = FindReservedItemInStorage();
                if (_targetItem == null)
                {
                    _isComplete = true;
                    return;
                }
                _targetInstance = _targetItem.ItemInstance;
                _state = StageState.MovingToItem;
                break;

            case StageState.MovingToItem:
                if (_targetItem == null || _targetItem.gameObject == null || _targetItem.IsBeingCarried)
                {
                    _state = StageState.Finding;
                    _targetItem = null;
                    _targetInstance = null;
                    return;
                }

                var itemCol = _targetItem.ItemInteractable?.InteractionZone
                              ?? _targetItem.GetComponentInChildren<Collider>();
                Vector3 itemPos = itemCol != null ? itemCol.bounds.ClosestPoint(worker.transform.position) : _targetItem.transform.position;

                HandleMovementTo(worker, itemPos, out bool arrivedAtItem, itemCol);
                if (arrivedAtItem)
                {
                    _state = StageState.PickingUp;
                    _actionStarted = false;
                }
                break;

            case StageState.PickingUp:
                if (_targetItem == null || _targetItem.gameObject == null)
                {
                    _state = StageState.Finding;
                    return;
                }

                if (!_actionStarted)
                {
                    var pickup = new CharacterPickUpItem(worker, _targetItem.ItemInstance, _targetItem.gameObject);
                    if (worker.CharacterActions.ExecuteAction(pickup))
                    {
                        _actionStarted = true;
                        pickup.OnActionFinished += () =>
                        {
                            _targetPos = ComputePickupDropPoint();
                            _state = StageState.MovingToPickup;
                            _actionStarted = false;
                        };
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[StageForPickup]</color> {worker.CharacterName} could not start PickUp. Retrying from Finding.");
                        _state = StageState.Finding;
                    }
                }
                break;

            case StageState.MovingToPickup:
                var pickupCol = _building.PickupZone != null ? _building.PickupZone.GetComponent<Collider>() : null;
                HandleMovementTo(worker, _targetPos, out bool arrivedAtPickup, pickupCol, bypassEarlyExit: true);

                if (!arrivedAtPickup && pickupCol != null)
                {
                    Vector3 flatWorkerPos = new Vector3(worker.transform.position.x, pickupCol.bounds.center.y, worker.transform.position.z);
                    Bounds safeBounds = pickupCol.bounds;
                    safeBounds.Expand(-0.6f);
                    if (safeBounds.Contains(flatWorkerPos))
                    {
                        worker.CharacterMovement.ResetPath();
                        arrivedAtPickup = true;
                    }
                }

                if (arrivedAtPickup)
                {
                    _state = StageState.DroppingOff;
                    _actionStarted = false;
                }
                break;

            case StageState.DroppingOff:
                if (!_actionStarted)
                {
                    ItemInstance carried = GetCarriedItem(worker);
                    if (carried == null)
                    {
                        // Item vanished during staging — let the outer system recover (RefreshStorageInventory
                        // + ReportMissingReservedItem paths already cover this).
                        _isComplete = true;
                        return;
                    }

                    var drop = new CharacterDropItem(worker, carried);
                    if (worker.CharacterActions.ExecuteAction(drop))
                    {
                        _actionStarted = true;
                        drop.OnActionFinished += () => FinishStaging(worker, carried);
                    }
                    else
                    {
                        // Action rejected (another action running) — clear & force-finish to avoid a deadlock.
                        if (worker.CharacterActions.CurrentAction != null)
                        {
                            worker.CharacterActions.ClearCurrentAction();
                        }
                        FinishStaging(worker, carried);
                    }
                }
                break;
        }
    }

    private void FinishStaging(Character worker, ItemInstance item)
    {
        // The item is now physically in PickupZone. Logically it was already reserved by the
        // TransportOrder — no re-add to _inventory is needed because CharacterDropItem removes
        // it from the worker AND the physical WorldItem sits in PickupZone where the transporter
        // will pick it up. RefreshStorageInventory's Pass 2 absorption is gated OUT of PickupZone
        // (see CommercialBuilding) so it won't double-count.
        Debug.Log($"<color=cyan>[StageForPickup]</color> {worker.CharacterName} staged {item?.ItemSO?.ItemName} at {_building?.BuildingName} PickupZone.");
        _actionStarted = false;
        _isComplete = true;
    }

    /// <summary>
    /// Reserved-instance scanner. Walks every live outbound TransportOrder on this
    /// building and returns the first physical WorldItem that:
    ///   (a) is currently inside StorageZone
    ///   (b) is NOT already inside PickupZone
    ///   (c) carries an ItemInstance that the TransportOrder has reserved.
    /// Returns null when nothing needs staging.
    /// </summary>
    private WorldItem FindReservedItemInStorage()
    {
        if (_building == null) return null;
        var logistics = _building.LogisticsManager;
        if (logistics == null) return null;

        // Outbound transports ARE the ones this building sources — PlacedTransportOrders is the
        // list of transports WE arranged (Source == _building). ActiveTransportOrders is for the
        // destination side. See BuildingLogisticsManager.DispatchTransportOrder → AddPlacedTransportOrder.
        HashSet<ItemInstance> reservedForOutgoing = new HashSet<ItemInstance>();
        foreach (var t in logistics.PlacedTransportOrders)
        {
            if (t == null || t.ReservedItems == null) continue;
            if (t.Source != _building) continue;
            if (t.IsCompleted) continue;
            foreach (var inst in t.ReservedItems) reservedForOutgoing.Add(inst);
        }

        if (reservedForOutgoing.Count == 0) return null;

        var storageCol = _building.StorageZone != null ? _building.StorageZone.GetComponent<Collider>() : null;
        if (storageCol == null) return null;
        var pickupCol = _building.PickupZone != null ? _building.PickupZone.GetComponent<Collider>() : null;

        // Prefer reserved physical items that are still in storage.
        foreach (var worldItem in _building.GetWorldItemsInStorage())
        {
            if (worldItem == null || worldItem.ItemInstance == null) continue;
            if (worldItem.IsBeingCarried) continue;
            if (!reservedForOutgoing.Contains(worldItem.ItemInstance)) continue;

            // Skip if somehow already physically in pickup (item sitting on the StorageZone/PickupZone border).
            if (pickupCol != null)
            {
                Vector3 flat = new Vector3(worldItem.transform.position.x, pickupCol.bounds.center.y, worldItem.transform.position.z);
                if (pickupCol.bounds.Contains(flat)) continue;
            }

            return worldItem;
        }

        return null;
    }

    private Vector3 ComputePickupDropPoint()
    {
        if (_building == null || _building.PickupZone == null) return _building != null ? _building.transform.position : Vector3.zero;
        var col = _building.PickupZone.GetComponent<Collider>();
        if (col == null) return _building.PickupZone.transform.position;

        Bounds b = col.bounds;
        float varX = Mathf.Max(b.extents.x * 0.5f, 0.1f);
        float varZ = Mathf.Max(b.extents.z * 0.5f, 0.1f);
        return b.center + new Vector3(Random.Range(-varX, varX), 0f, Random.Range(-varZ, varZ));
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

        bool hasPathFailed = NavMeshUtility.HasPathFailed(movement, 0, 0.2f);
        if (!movement.HasPath || hasPathFailed)
        {
            if (targetCollider != null)
            {
                bool blacklisted = worker.PathingMemory.RecordFailure(targetCollider.gameObject.GetInstanceID());
                if (blacklisted)
                {
                    movement.Stop();
                    movement.ResetPath();
                    _isComplete = true;
                    return;
                }
            }
        }

        if (!movement.HasPath)
        {
            movement.SetDestination(targetPos);
            return;
        }

        if (movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            float distance = Vector3.Distance(
                new Vector3(worker.transform.position.x, 0, worker.transform.position.z),
                new Vector3(targetPos.x, 0, targetPos.z));

            if (targetCollider == null && distance > movement.StoppingDistance + 0.5f)
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

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _actionStarted = false;
        _state = StageState.Finding;
        _targetItem = null;
        _targetInstance = null;
    }
}
