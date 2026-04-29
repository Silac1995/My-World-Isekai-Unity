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

    // Furniture-first deposit. When non-null, the worker is heading to (or already
    // at) a StorageFurniture slot and will queue CharacterStoreInFurnitureAction
    // instead of CharacterDropItem in the DroppingOff state. Resolved per-item in
    // DetermineStoragePosition() — re-evaluated after every successful deposit so
    // that a multi-item delivery can fan out across several furniture pieces and
    // fall back to the loose StorageZone drop only when nothing fits.
    private StorageFurniture _targetFurniture;

    // Real-time stamp captured when MovingToStorage begins targeting a furniture.
    // If the worker hasn't arrived within FurnitureMoveTimeoutSeconds we give up on
    // the furniture (typical cause: missing _interactionPoint on the prefab → trying
    // to walk into the NavMeshObstacle carve). Falls back to the loose zone drop so
    // the cycle can never softlock.
    private float _furnitureMoveStartedAt = -1f;
    private const float FurnitureMoveTimeoutSeconds = 5f;

    // One-shot per-state-transition logging support (set NPCDebug.VerboseActions=true
    // to surface state churn in the Console).
    private GatherState _lastLoggedState = (GatherState)(-1);

    // Furniture instances we already failed to reach during this action invocation.
    // Survives only for the action's lifetime (cleared in Exit). Without this, the
    // timeout fallback would re-pick the same unreachable furniture next tick because
    // FindStorageFurnitureForItem doesn't consult PathingMemory.
    private HashSet<StorageFurniture> _excludedFurniture;

    // ── FindLooseWorldItem cache (perf, see wiki/projects/optimisation-backlog.md entry #2 / Bₐ).
    // FindLooseWorldItem runs 3 Physics.OverlapBox + per-collider component scans + List
    // alloc + AddRange on every IsValid() call. With 4 logistics managers + 2 harvesters
    // each running this per Job.Execute tick, that's hundreds of redundant PhysX scans/sec.
    // Cache the result per-action-instance for 0.5 s — long enough to absorb the Cₐ-throttled
    // 0.3 s job tick. If another worker grabs our target before we get there, the action's
    // existing failure-handling (target null, retry next tick) catches it.
    private const float FindLooseCacheTTLSeconds = 0.5f;
    private float _lastFindLooseTime = -1f;
    private WorldItem _lastFindLooseResult;
    private int _lastFindLooseFrame = -1;

    // Reused PhysX overlap buffer + scratch list, shared across action instances (PhysX
    // queries are main-thread, so a static is safe and avoids per-instance allocations).
    // 128 covers typical zone clutter; saturation falls back to truncated scan with warning.
    private const int OverlapBufferSize = 128;
    private static readonly Collider[] s_overlapBuffer = new Collider[OverlapBufferSize];
    private static readonly List<Collider> s_scratchCollidersList = new List<Collider>(OverlapBufferSize * 3);

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

        // One-shot transition log so you can see exactly which state is sticking.
        // Gated behind NPCDebug.VerboseActions to avoid log spam in normal play.
        if (NPCDebug.VerboseActions && _currentState != _lastLoggedState)
        {
            ItemInstance dbgCarried = GetCarriedItem(worker);
            Debug.Log($"<color=#88aaff>[GatherDBG]</color> {worker.CharacterName} state {_lastLoggedState} → {_currentState} | targetFurniture={(_targetFurniture != null ? _targetFurniture.FurnitureName : "<none>")} | targetPos={_targetPos} | carrying={(dbgCarried != null ? dbgCarried.ItemSO.ItemName : "<none>")} | dist={Vector3.Distance(worker.transform.position, _targetPos):F2}");
            _lastLoggedState = _currentState;
        }

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
                // If we are empty-handed AND we have a pending order, we should stop harvesting and allow re-planning (so we prioritize PlaceOrder).
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
                bool arrivedAtStorage;
                if (_targetFurniture != null)
                {
                    // Stamp the start time the first tick we enter MovingToStorage with a furniture target.
                    if (_furnitureMoveStartedAt < 0f)
                    {
                        _furnitureMoveStartedAt = Time.unscaledTime;
                    }

                    // Furniture target: there's no zone collider to early-exit on, so
                    // arrival is a flat-XZ proximity check against the furniture's
                    // interaction point. 1.5f matches HandleMovementTo's early-exit
                    // threshold so behaviour stays consistent with the zone path.
                    HandleMovementTo(worker, _targetPos, out arrivedAtStorage, null);

                    if (!arrivedAtStorage)
                    {
                        Vector3 flatWorker = new Vector3(worker.transform.position.x, 0f, worker.transform.position.z);
                        Vector3 flatTarget = new Vector3(_targetPos.x, 0f, _targetPos.z);
                        if (Vector3.Distance(flatWorker, flatTarget) <= 1.5f)
                        {
                            worker.CharacterMovement.ResetPath();
                            arrivedAtStorage = true;
                        }
                    }

                    // Softlock guard: if we've spent too long pathing to a furniture without
                    // arriving, abandon it and fall back to the loose zone drop. Most common
                    // cause: the StorageFurniture has no _interactionPoint Transform assigned,
                    // so GetInteractionPosition() returns the crate centre — which is inside
                    // the NavMeshObstacle carve and unreachable.
                    if (!arrivedAtStorage && Time.unscaledTime - _furnitureMoveStartedAt > FurnitureMoveTimeoutSeconds)
                    {
                        Debug.LogWarning($"<color=orange>[GOAP Storage]</color> {worker.CharacterName} could not reach {_targetFurniture.FurnitureName} after {FurnitureMoveTimeoutSeconds}s (stuck at dist={Vector3.Distance(worker.transform.position, _targetPos):F2}). Excluding it for the rest of this action. CHECK: does {_targetFurniture.FurnitureName} have an _interactionPoint Transform assigned in the prefab?");
                        if (_excludedFurniture == null) _excludedFurniture = new HashSet<StorageFurniture>();
                        _excludedFurniture.Add(_targetFurniture);
                        worker.CharacterMovement.Stop();
                        worker.CharacterMovement.ResetPath();
                        // Re-pick a target: next compatible furniture, or the zone if none.
                        DetermineStoragePosition();
                        // Stay in MovingToStorage; next tick walks to the new spot.
                    }
                }
                else
                {
                    Zone storageZone = _building.StorageZone != null ? _building.StorageZone : _building.MainRoom.GetComponent<Zone>();
                    Collider storageCol = storageZone != null ? storageZone.GetComponent<Collider>() : null;

                    HandleMovementTo(worker, _targetPos, out arrivedAtStorage, storageCol, true);

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
                }

                if (arrivedAtStorage)
                {
                    _currentState = GatherState.DroppingOff;
                    _actionStarted = false;
                    _furnitureMoveStartedAt = -1f; // ready for the next furniture leg
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

                    // Furniture-first: if we walked here for a specific furniture and it
                    // STILL has space for the item we're holding, queue the slot insert
                    // instead of the loose drop. Re-validates lock + space because another
                    // worker (or the player) may have filled the slot since we arrived.
                    if (_targetFurniture != null
                        && !_targetFurniture.IsLocked
                        && _targetFurniture.HasFreeSpaceForItem(carriedItem))
                    {
                        var storeAction = new CharacterStoreInFurnitureAction(worker, carriedItem, _targetFurniture);

                        if (worker.CharacterActions.ExecuteAction(storeAction))
                        {
                            _actionStarted = true;
                            // Capture the furniture reference at queue time — _targetFurniture
                            // may be re-pointed by FinishDropoff below before this lambda fires.
                            StorageFurniture furnitureRef = _targetFurniture;
                            storeAction.OnActionFinished += () => FinishDropoff(worker, carriedItem, furnitureRef);
                        }
                        else
                        {
                            // Action refused (already busy). Mirror the legacy drop fallback:
                            // purge any phantom action and force-finish the logical state.
                            if (worker.CharacterActions.CurrentAction != null)
                            {
                                worker.CharacterActions.ClearCurrentAction();
                            }
                            RemoveItemFromWorker(worker, carriedItem);
                            FinishDropoff(worker, carriedItem, _targetFurniture);
                        }
                        return;
                    }

                    // No furniture target (or it filled up between MovingToStorage and now)
                    // — fall back to the original loose-drop path inside StorageZone.
                    var dropAction = new CharacterDropItem(worker, carriedItem);

                    // Si true: L'action a été acceptée et l'animation commence.
                    if (worker.CharacterActions.ExecuteAction(dropAction))
                    {
                        _actionStarted = true;
                        dropAction.OnActionFinished += () => FinishDropoff(worker, carriedItem, null);
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
                        FinishDropoff(worker, carriedItem, null);
                    }
                }
                break;
        }
    }

    private void DetermineStoragePosition()
    {
        // Furniture-first preference: if the worker is currently carrying an item AND
        // the building has a StorageFurniture that accepts it, head straight to the
        // furniture's interaction point. Otherwise fall back to the loose StorageZone
        // drop — same behaviour as before this refactor.
        _targetFurniture = null;
        _furnitureMoveStartedAt = -1f;

        Character worker = _manager != null ? _manager.Worker : null;
        ItemInstance carriedItem = worker != null ? GetCarriedItem(worker) : null;

        if (carriedItem != null && _building != null)
        {
            // Walk furniture in declaration order, skipping any we've already failed to reach.
            foreach (var candidate in _building.GetFurnitureOfType<StorageFurniture>())
            {
                if (candidate == null || candidate.IsLocked) continue;
                if (_excludedFurniture != null && _excludedFurniture.Contains(candidate)) continue;
                if (!candidate.HasFreeSpaceForItem(carriedItem)) continue;

                _targetFurniture = candidate;
                // Worker-aware overload: when the candidate has no _interactionPoint
                // authored, this lands the target on the closest InteractionZone face
                // to the worker — i.e. on the navmesh-walkable side.
                _targetPos = worker != null
                    ? candidate.GetInteractionPosition(worker.transform.position)
                    : candidate.GetInteractionPosition();
                return;
            }
        }

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

    private void FinishDropoff(Character worker, ItemInstance item, StorageFurniture depositedInFurniture)
    {
        _building.AddToInventory(item);
        if (depositedInFurniture != null)
        {
            Debug.Log($"<color=green>[Harvesting]</color> {worker.CharacterName} a rangé {item.ItemSO.ItemName} dans {depositedInFurniture.FurnitureName}.");
        }
        else
        {
            Debug.Log($"<color=green>[Harvesting]</color> {worker.CharacterName} a rangé {item.ItemSO.ItemName} dans le stockage.");
        }

        var bManager = _building?.LogisticsManager;
        if (bManager != null)
        {
            bManager.OnItemHarvested(item.ItemSO);
        }

        // On libère le verrou d'action
        _actionStarted = false;

        // Multi-item delivery support: peek at the next carried item BEFORE we stay
        // in DroppingOff. If the previously-targeted furniture is now full / wrong-fit
        // for the next item, re-run DetermineStoragePosition() and walk again. If the
        // worker is empty-handed, the next DroppingOff tick falls through to FindingItem.
        ItemInstance nextItem = GetCarriedItem(worker);
        if (nextItem != null)
        {
            StorageFurniture nextFurniture = _building != null ? _building.FindStorageFurnitureForItem(nextItem) : null;

            // If the next pick is a different furniture (or a zone-fallback now that
            // storage furniture is full for that item type), we have to walk to the new
            // spot — kick back to MovingToStorage so HandleMovementTo runs.
            if (nextFurniture != _targetFurniture)
            {
                DetermineStoragePosition();
                _currentState = GatherState.MovingToStorage;
            }
            // Same furniture (or both null = same loose-zone target): stay in
            // DroppingOff — next tick we'll queue the next item without moving.
        }

        // On reste dans l'état DroppingOff (ou MovingToStorage si la cible a changé).
        // Au prochain frame, le switch traitera l'item suivant ou retournera à FindingItem
        // si le sac est vide.
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

        // Cache hit: re-use the previous result if it's still fresh AND the cached item
        // hasn't been picked up since (validate the WorldItem still exists and is loose).
        // Cleared on Exit() so a new action invocation always starts cold.
        if (UnityEngine.Time.time - _lastFindLooseTime < FindLooseCacheTTLSeconds)
        {
            if (_lastFindLooseResult == null) return null;
            // Cached item still valid — same WorldItem might have been picked up by
            // another worker; skip the cache if so to force a refresh.
            if (_lastFindLooseResult.gameObject != null && !_lastFindLooseResult.IsBeingCarried)
            {
                return _lastFindLooseResult;
            }
        }

        BoxCollider boxCol = _building.BuildingZone.GetComponent<BoxCollider>();
        if (boxCol == null) return null;

        WorldItem nearest = null;
        float nearestDist = float.MaxValue;

        Zone storageZone = _building.StorageZone;
        BoxCollider storageCol = storageZone != null ? storageZone.GetComponent<BoxCollider>() : null;

        Zone depositZone = null;
        if (_building is HarvestingBuilding harvestingBuilding)
        {
            depositZone = harvestingBuilding.DepositZone;
        }
        BoxCollider depositCol = depositZone != null ? depositZone.GetComponent<BoxCollider>() : null;

        Zone deliveryZone = _building.DeliveryZone;
        BoxCollider deliveryCol = deliveryZone != null ? deliveryZone.GetComponent<BoxCollider>() : null;

        // Phase-A: items sitting in PickupZone are OUTBOUND staging — they were deliberately
        // moved there by GoapAction_StageItemForPickup and must NOT be re-gathered back into
        // storage (that would undo the staging every tick). Defensive skip below.
        Zone pickupZone = _building.PickupZone;
        BoxCollider pickupCol = pickupZone != null ? pickupZone.GetComponent<BoxCollider>() : null;

        // Reused scratch list across all 3 zone scans (NOT static-only; cleared each call).
        // OverlapBoxNonAlloc against the shared buffer to avoid per-call Collider[] allocations.
        // See wiki/projects/optimisation-backlog.md entry #2 / Bₐ.
        s_scratchCollidersList.Clear();

        AppendOverlapBoxColliders(boxCol, s_scratchCollidersList, "BuildingZone");
        if (depositCol != null) AppendOverlapBoxColliders(depositCol, s_scratchCollidersList, "DepositZone");
        if (deliveryCol != null) AppendOverlapBoxColliders(deliveryCol, s_scratchCollidersList, "DeliveryZone");

        for (int i = 0; i < s_scratchCollidersList.Count; i++)
        {
            var col = s_scratchCollidersList[i];
            if (col == null) continue;

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

            // Phase-A: items already staged in PickupZone are outbound reservations; skip.
            if (pickupCol != null)
            {
                Vector3 flatPickup = new Vector3(worldItem.transform.position.x, pickupCol.bounds.center.y, worldItem.transform.position.z);
                if (pickupCol.bounds.Contains(flatPickup)) continue;
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

        s_scratchCollidersList.Clear();
        _lastFindLooseTime = UnityEngine.Time.time;
        _lastFindLooseResult = nearest;
        return nearest;
    }

    /// <summary>
    /// Runs <see cref="Physics.OverlapBoxNonAlloc"/> against the shared static buffer
    /// and appends the results into <paramref name="dest"/>. Logs a saturation warning
    /// (rule #31) if the buffer fills exactly, signalling that <see cref="OverlapBufferSize"/>
    /// should be bumped.
    /// </summary>
    private static void AppendOverlapBoxColliders(BoxCollider boxCol, List<Collider> dest, string zoneTag)
    {
        Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
        Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

        int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, s_overlapBuffer, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
        if (hitCount == OverlapBufferSize)
        {
            Debug.LogWarning($"[GoapAction_GatherStorageItems] {zoneTag} on '{boxCol.name}' saturated the OverlapBox buffer ({OverlapBufferSize}) — bump OverlapBufferSize. Items beyond #{OverlapBufferSize} truncated this scan.", boxCol);
        }
        for (int i = 0; i < hitCount; i++)
        {
            var col = s_overlapBuffer[i];
            if (col != null) dest.Add(col);
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _actionStarted = false;
        _currentState = GatherState.FindingItem;
        _targetItem = null;
        _targetFurniture = null;
        _furnitureMoveStartedAt = -1f;
        _excludedFurniture?.Clear();
        _lastLoggedState = (GatherState)(-1);
        // Reset the FindLooseWorldItem cache so the next invocation starts cold.
        _lastFindLooseTime = -1f;
        _lastFindLooseResult = null;
    }
}
