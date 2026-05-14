using System.Collections.Generic;
using UnityEngine;
using MWI.AI;

/// <summary>
/// GOAP action owned by <see cref="JobLogisticsManager"/>. When the workplace is a
/// <see cref="ShopBuilding"/>, walks every non-SellShelf <see cref="StorageFurniture"/>
/// in the shop, finds any <see cref="ItemInstance"/> that matches the shop's sale
/// <see cref="ShopBuilding.Catalog"/>, takes it into hands, then walks to a
/// <see cref="StorageRoleType.SellShelf"/> with free capacity and stores it.
///
/// Pairs with the routing-side fix in
/// <see cref="CommercialBuilding.FindStorageFurnitureForItem"/>: new supplier
/// deliveries of sale items land on a SellShelf directly. This action heals the
/// EXISTING state — items already misplaced in InventoryStorage (or any other
/// non-SellShelf role) on punch-in.
///
/// Cost: 0.3f. Cheaper than <c>GoapAction_GatherStorageItems</c> (0.5f) so the
/// planner picks shelf-restocking before generic tidying. More expensive than
/// <c>GoapAction_StageItemForPickup</c> (0.2f) and <c>GoapAction_PlaceOrder</c>
/// (0.1f) — outbound transports and order placement remain higher priority.
///
/// Single-responsibility note: deliberately NOT folded into
/// <c>GoapAction_GatherStorageItems</c>. That action moves LOOSE WorldItems
/// (the building floor / DeliveryZone) into storage. This one moves SLOT-stored
/// items between two pieces of storage furniture. Same verbs (take, walk, store)
/// — different source primitive (slot vs. WorldItem) + different trigger
/// condition (catalog mismatch vs. loose item present). Keeping them separate
/// avoids the bool-flag sprawl that always decays into untestable branching.
///
/// Anti-thrash: once an item is in our hands, the planner can't re-target it
/// because the source slot is already empty. The IsValid scan re-runs against
/// the current slot state so two workers on the same shift will pick distinct
/// items (or the second exits cleanly when none remain).
/// </summary>
public class GoapAction_RestockSellShelves : GoapAction
{
    public override string ActionName => "RestockSellShelves";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isIdling", true } // satisfies the Idle goal so the planner considers this when nothing urgent is pending
    };

    public override float Cost => 0.3f;

    private readonly JobLogisticsManager _manager;
    private readonly CommercialBuilding _building;
    private readonly ShopBuilding _shop;

    private StorageFurniture _sourceFurniture;
    private ItemInstance _sourceItem;
    private StorageFurniture _targetShelf;
    private bool _isComplete;
    private bool _actionStarted;
    private RestockState _state = RestockState.Finding;
    private Vector3 _targetPos;

    // Sources we already failed to reach this invocation (mirrors the pattern in
    // GoapAction_GatherStorageItems). Cleared in Exit. Without this, a furniture
    // with a bad interaction point would re-pick on every retry tick.
    private HashSet<StorageFurniture> _excludedSources;
    private HashSet<StorageFurniture> _excludedShelves;

    private const float MoveTimeoutSeconds = 5f;
    private float _moveStartedAt = -1f;

    private enum RestockState
    {
        Finding,            // pick a (sourceFurniture, sourceItem, targetShelf) triple
        MovingToSource,
        TakingFromSource,   // CharacterTakeFromFurnitureAction
        MovingToShelf,
        StoringOnShelf      // CharacterStoreInFurnitureAction
    }

    public GoapAction_RestockSellShelves(JobLogisticsManager manager)
    {
        _manager = manager;
        _building = manager?.Workplace;
        _shop = _building as ShopBuilding;
    }

    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_shop == null) return false;
        if (_actionStarted) return true;

        // Holding a sale item already — finish staging it before re-planning. This
        // path must run regardless of the dirty flag: the carried item came from a
        // prior tick's scan that DID find work, and bailing here would strand the
        // item in hands.
        ItemInstance carried = GetCarriedItem(worker);
        if (carried != null && _shop.GetCatalogEntry(carried.ItemSO) != null) return true;

        // Dirty-flag gate (rule #34): on a stable shop (no inventory mutation, no
        // catalog edit, no role flip, no per-storage slot change since the last
        // clean walk) IsValid short-circuits with a single field read. Matches the
        // canonical LogisticsOrderBook._dispatchDirty shape.
        var logistics = _shop.LogisticsManager;
        if (logistics != null && !logistics.IsRestockDirty()) return false;

        // Walk the slot graph. This is the expensive path:
        //   GetItemsInStorageFurniture()  (slot enumeration across rooms)
        //   × FindShelfWithSpace(...)     (HasFreeSpaceForItem per SellShelf)
        // Cleared below only if the walk proves zero candidates; if a candidate is
        // found we leave the flag dirty so further candidates (after this one is
        // moved) are still re-scanned on the next tick.
        bool foundCandidate = FindMisplacedSaleItem(worker, out _sourceFurniture, out _sourceItem, out _targetShelf);
        if (!foundCandidate && logistics != null)
        {
            logistics.ClearRestockDirty();
        }
        return foundCandidate;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        var movement = worker.CharacterMovement;
        if (movement == null) return;

        switch (_state)
        {
            case RestockState.Finding:
                // Already carrying a sale item? Skip the source-take and go straight
                // to the shelf — common path when IsValid re-validated mid-flight.
                ItemInstance carried = GetCarriedItem(worker);
                if (carried != null && _shop.GetCatalogEntry(carried.ItemSO) != null)
                {
                    _targetShelf = FindShelfWithSpace(carried, _excludedShelves);
                    if (_targetShelf == null)
                    {
                        // No shelf has room — abandon the action. The item stays in hands;
                        // GoapAction_GatherStorageItems (or the next plan) will re-route it
                        // via the routing-side rule, which falls through to InventoryStorage.
                        if (NPCDebug.VerboseJobs)
                        {
                            Debug.Log($"<color=#88aaff>[Restock]</color> {worker.CharacterName}: carrying {carried.ItemSO.ItemName} but no SellShelf has free space at {_shop.BuildingName}. Releasing back to general flow.");
                        }
                        _isComplete = true;
                        return;
                    }
                    _targetPos = _targetShelf.GetInteractionPosition(worker.transform.position);
                    _state = RestockState.MovingToShelf;
                    return;
                }

                if (!FindMisplacedSaleItem(worker, out _sourceFurniture, out _sourceItem, out _targetShelf))
                {
                    _isComplete = true;
                    return;
                }
                _targetPos = _sourceFurniture.GetInteractionPosition(worker.transform.position);
                _state = RestockState.MovingToSource;
                _moveStartedAt = -1f;
                break;

            case RestockState.MovingToSource:
                if (_sourceFurniture == null || _sourceItem == null)
                {
                    _state = RestockState.Finding;
                    return;
                }

                MoveTowards(worker, _targetPos, out bool arrivedAtSource);
                if (arrivedAtSource)
                {
                    _state = RestockState.TakingFromSource;
                    _actionStarted = false;
                    _moveStartedAt = -1f;
                }
                else if (HasMoveTimedOut())
                {
                    if (NPCDebug.VerboseJobs)
                    {
                        Debug.LogWarning($"<color=orange>[Restock]</color> {worker.CharacterName} could not reach source furniture '{_sourceFurniture?.FurnitureName}' after {MoveTimeoutSeconds}s — excluding and re-finding.");
                    }
                    if (_excludedSources == null) _excludedSources = new HashSet<StorageFurniture>();
                    _excludedSources.Add(_sourceFurniture);
                    worker.CharacterMovement.Stop();
                    worker.CharacterMovement.ResetPath();
                    _sourceFurniture = null;
                    _sourceItem = null;
                    _state = RestockState.Finding;
                    _moveStartedAt = -1f;
                }
                break;

            case RestockState.TakingFromSource:
                if (_sourceFurniture == null || _sourceItem == null)
                {
                    _state = RestockState.Finding;
                    return;
                }

                // Re-validate the slot still holds our target (another worker may
                // have stolen it between MovingToSource and now).
                if (!FurnitureStillContains(_sourceFurniture, _sourceItem))
                {
                    if (NPCDebug.VerboseJobs)
                    {
                        Debug.Log($"<color=#88aaff>[Restock]</color> {worker.CharacterName}: source slot no longer has {_sourceItem?.ItemSO?.ItemName} — re-finding.");
                    }
                    _sourceFurniture = null;
                    _sourceItem = null;
                    _state = RestockState.Finding;
                    return;
                }

                if (!_actionStarted)
                {
                    var take = new CharacterTakeFromFurnitureAction(worker, _sourceItem, _sourceFurniture);
                    if (worker.CharacterActions.ExecuteAction(take))
                    {
                        _actionStarted = true;
                        ItemInstance takenInstance = _sourceItem;
                        take.OnActionFinished += () =>
                        {
                            if (take.Taken)
                            {
                                // Re-pick the shelf at arrival time — capacity may have shifted.
                                _targetShelf = FindShelfWithSpace(takenInstance, _excludedShelves);
                                if (_targetShelf == null)
                                {
                                    // Nobody has room — bail. Item is in hands; the
                                    // post-action plan will fall back to generic Gather.
                                    if (NPCDebug.VerboseJobs)
                                    {
                                        Debug.LogWarning($"<color=orange>[Restock]</color> {worker.CharacterName}: took {takenInstance.ItemSO.ItemName} but no SellShelf has free space — letting Gather take over.");
                                    }
                                    _isComplete = true;
                                    _actionStarted = false;
                                    return;
                                }
                                _targetPos = _targetShelf.GetInteractionPosition(worker.transform.position);
                                _state = RestockState.MovingToShelf;
                            }
                            else
                            {
                                _state = RestockState.Finding;
                            }
                            _actionStarted = false;
                        };
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[Restock]</color> {worker.CharacterName}: TakeFromFurniture refused. Re-finding.");
                        _state = RestockState.Finding;
                    }
                }
                break;

            case RestockState.MovingToShelf:
                if (_targetShelf == null)
                {
                    _state = RestockState.Finding;
                    return;
                }

                MoveTowards(worker, _targetPos, out bool arrivedAtShelf);
                if (arrivedAtShelf)
                {
                    _state = RestockState.StoringOnShelf;
                    _actionStarted = false;
                    _moveStartedAt = -1f;
                }
                else if (HasMoveTimedOut())
                {
                    if (NPCDebug.VerboseJobs)
                    {
                        Debug.LogWarning($"<color=orange>[Restock]</color> {worker.CharacterName} could not reach shelf '{_targetShelf?.FurnitureName}' after {MoveTimeoutSeconds}s — excluding and re-picking.");
                    }
                    if (_excludedShelves == null) _excludedShelves = new HashSet<StorageFurniture>();
                    _excludedShelves.Add(_targetShelf);
                    worker.CharacterMovement.Stop();
                    worker.CharacterMovement.ResetPath();
                    _targetShelf = null;
                    _state = RestockState.Finding;
                    _moveStartedAt = -1f;
                }
                break;

            case RestockState.StoringOnShelf:
                if (_targetShelf == null)
                {
                    _state = RestockState.Finding;
                    return;
                }

                if (!_actionStarted)
                {
                    ItemInstance carriedNow = GetCarriedItem(worker);
                    if (carriedNow == null)
                    {
                        // Item vanished somehow — bail.
                        _isComplete = true;
                        return;
                    }

                    // Re-validate shelf capacity at arrival.
                    if (_targetShelf.IsLocked || !_targetShelf.HasFreeSpaceForItem(carriedNow))
                    {
                        if (_excludedShelves == null) _excludedShelves = new HashSet<StorageFurniture>();
                        _excludedShelves.Add(_targetShelf);
                        _targetShelf = null;
                        _state = RestockState.Finding;
                        return;
                    }

                    var store = new CharacterStoreInFurnitureAction(worker, carriedNow, _targetShelf);
                    if (worker.CharacterActions.ExecuteAction(store))
                    {
                        _actionStarted = true;
                        StorageFurniture shelfRef = _targetShelf;
                        ItemInstance storedInstance = carriedNow;
                        store.OnActionFinished += () =>
                        {
                            Debug.Log($"<color=green>[Restock]</color> {worker.CharacterName} moved {storedInstance.ItemSO.ItemName} → {shelfRef.FurnitureName}.");
                            _isComplete = true;
                            _actionStarted = false;
                            // Re-mark dirty so the next planner tick re-evaluates: there might
                            // be more catalog items still misplaced after this one. The
                            // CharacterStoreInFurnitureAction itself already mutates
                            // StorageFurniture.OnInventoryChanged on both source and
                            // destination — that fires our per-storage hook — but doing it
                            // explicitly here makes the contract obvious and survives any
                            // future refactor that decouples the slot transfer from the
                            // event. Idempotent with the event-driven set.
                            if (_shop != null && _shop.LogisticsManager != null)
                            {
                                _shop.LogisticsManager.MarkRestockDirty();
                            }
                        };
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[Restock]</color> {worker.CharacterName}: Store refused at {_targetShelf.FurnitureName}. Re-finding.");
                        _state = RestockState.Finding;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Walks every (furniture, slotItem) pair on the shop and returns the first
    /// triple that satisfies all of:
    ///   (a) furniture.Role != SellShelf (the item is misplaced)
    ///   (b) furniture is unlocked + not in the excluded-sources set
    ///   (c) slotItem.ItemSO is in the shop's sale Catalog
    ///   (d) at least one SellShelf has free space for slotItem
    /// Returns false when nothing qualifies — caller should mark the action complete.
    /// Avoids LINQ — rule #34 (no per-frame allocations).
    /// </summary>
    private bool FindMisplacedSaleItem(Character worker, out StorageFurniture src, out ItemInstance item, out StorageFurniture shelf)
    {
        src = null; item = null; shelf = null;
        if (_shop == null) return false;

        foreach (var (furniture, instance) in _shop.GetItemsInStorageFurniture())
        {
            if (furniture == null || instance == null) continue;
            if (furniture.Role == StorageRoleType.SellShelf) continue;       // already on a shelf — skip
            if (furniture.IsLocked) continue;
            if (_excludedSources != null && _excludedSources.Contains(furniture)) continue;
            if (_shop.GetCatalogEntry(instance.ItemSO) == null) continue;    // not for sale

            // Reservation guard: don't yank items reserved for an outbound TransportOrder.
            // GoapAction_StageItemForPickup is responsible for those; double-handling them
            // would race the staging path. Mirrors the reservation set used by
            // GoapAction_StageItemForPickup.CollectReservedOutgoingInstances.
            if (IsReservedForOutbound(instance)) continue;

            var candidateShelf = FindShelfWithSpace(instance, _excludedShelves);
            if (candidateShelf == null) continue;                            // no room — try next slot

            src = furniture;
            item = instance;
            shelf = candidateShelf;
            return true;
        }
        return false;
    }

    private StorageFurniture FindShelfWithSpace(ItemInstance forItem, HashSet<StorageFurniture> excluded)
    {
        if (_shop == null || forItem == null) return null;
        var shelves = _shop.GetStoragesWithRole(StorageRoleType.SellShelf);
        for (int i = 0; i < shelves.Count; i++)
        {
            var s = shelves[i];
            if (s == null || s.IsLocked) continue;
            if (excluded != null && excluded.Contains(s)) continue;
            if (s.HasFreeSpaceForItem(forItem)) return s;
        }
        return null;
    }

    private bool IsReservedForOutbound(ItemInstance instance)
    {
        var logistics = _shop != null ? _shop.LogisticsManager : null;
        if (logistics == null) return false;
        foreach (var t in logistics.PlacedTransportOrders)
        {
            if (t == null || t.ReservedItems == null) continue;
            if (t.Source != _shop) continue;
            if (t.IsCompleted) continue;
            if (t.ReservedItems.Contains(instance)) return true;
        }
        return false;
    }

    private static bool FurnitureStillContains(StorageFurniture furniture, ItemInstance instance)
    {
        var slots = furniture?.ItemSlots;
        if (slots == null) return false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null && slots[i].ItemInstance == instance) return true;
        }
        return false;
    }

    private static ItemInstance GetCarriedItem(Character worker)
    {
        var inventory = worker.CharacterEquipment?.GetInventory();
        if (inventory != null && inventory.ItemSlots.Exists(s => !s.IsEmpty()))
        {
            return inventory.ItemSlots.FindLast(s => !s.IsEmpty()).ItemInstance;
        }
        return worker.CharacterVisual?.BodyPartsController?.HandsController?.CarriedItem;
    }

    private void MoveTowards(Character worker, Vector3 targetPos, out bool arrived)
    {
        arrived = false;
        var movement = worker.CharacterMovement;

        // Stamp the start time the first tick we begin moving so the timeout fires.
        if (_moveStartedAt < 0f) _moveStartedAt = Time.unscaledTime;

        if (movement.PathPending) return;

        // No collider to early-exit on (furniture interaction points are bare positions).
        // Use flat-XZ proximity matching the threshold used in GoapAction_StageItemForPickup.
        Vector3 flatWorker = new Vector3(worker.transform.position.x, 0f, worker.transform.position.z);
        Vector3 flatTarget = new Vector3(targetPos.x, 0f, targetPos.z);
        if (Vector3.Distance(flatWorker, flatTarget) <= 1.5f)
        {
            movement.ResetPath();
            arrived = true;
            return;
        }

        if (!movement.HasPath)
        {
            movement.SetDestination(targetPos);
            return;
        }

        if (movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            float dist = Vector3.Distance(flatWorker, flatTarget);
            if (dist > movement.StoppingDistance + 0.5f)
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

    private bool HasMoveTimedOut() => _moveStartedAt > 0f && Time.unscaledTime - _moveStartedAt > MoveTimeoutSeconds;

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _actionStarted = false;
        _state = RestockState.Finding;
        _sourceFurniture = null;
        _sourceItem = null;
        _targetShelf = null;
        _moveStartedAt = -1f;
        _excludedSources?.Clear();
        _excludedShelves?.Clear();
    }
}
