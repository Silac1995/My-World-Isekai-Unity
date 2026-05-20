using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Job de type Harvester : récolte des ressources selon ce que le HarvestingBuilding dicte.
/// Utilise le système GOAP pour planifier ses actions :
/// 1. Explorer pour trouver une zone de Harvestable (si pas encore trouvée)
/// 2. Se rendre à la zone et récolter
/// 3. Déposer les ressources à la zone de dépôt
/// Puis recommencer le cycle.
/// </summary>
[System.Serializable]
public class JobHarvester : Job
{
    [SerializeField] private string _jobTitle;
    [SerializeField] private JobType _jobType;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Harvester;
    public override JobType Type => _jobType;

    // Heavy-planning job: GOAP plan + per-tick task scans against the building's
    // BuildingTaskManager. Reactivity comes from the GOAP plan itself (which still
    // ticks per-frame inside CharacterMovement / CharacterActions); 3.3 Hz here is
    // plenty for the planning layer. See wiki/projects/optimisation-backlog.md
    // entry #2 / Cₐ.
    public override float ExecuteIntervalSeconds => 0.3f;

    // GOAP
    private GoapGoal _harvestGoal;
    private List<GoapAction> _availableActions;
    // Pre-filtered subset of _availableActions where IsValid(worker) is true at planning time.
    // GoapPlanner.Plan does NOT call IsValid — it only matches Preconditions/Effects against the
    // world-state dict and picks the lowest-cost chain. Without this filter, two actions with
    // identical Preconditions+Effects but different Cost (e.g. HarvestResources@1 vs
    // DestroyHarvestable@5) make the planner always pick the cheaper one, even when its IsValid
    // returns false (no claimable HarvestResourceTask). The action then completes immediately in
    // Execute → planner replans → picks it again → infinite Idle/HarvestResources loop. Mirrors
    // JobLogisticsManager._scratchValidActions.
    private List<GoapAction> _scratchValidActions = new List<GoapAction>(6);
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    // Per-tick allocation pool (see PlanNextActions). Without these, each tick allocated a
    // new worldState dict, a new List of 5 new GoapAction objects, and two new GoapGoal instances
    // with their own dicts — a dominant GC source under heavy worker load.
    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(6);
    private GoapGoal _cachedIdleGoal;
    private GoapGoal _cachedHarvestAndDepositGoal;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _harvestGoal != null ? _harvestGoal.GoalName : "No Goal";

    public JobHarvester(string jobTitle = "Harvester", JobType jobType = JobType.None)
    {
        _jobTitle = jobTitle;
        _jobType = jobType;
    }

    // ── CityHarvester runtime branch (Plan 4b Task 7) ────────────────────────
    //
    // When _workplace is an AdministrativeBuilding, the harvester runs a manual
    // state machine instead of the HarvestingBuilding-driven GOAP planner. It reads
    // AB.GetUnfulfillableHarvestQueue() for materials the logistics chain failed to
    // source, scans for a nearby Harvestable yielding the wanted item, runs the
    // harvest → pickup → walk-to-AB-storage → deposit cycle, and decrements the
    // queue. The existing GOAP actions can't be reused here because they take
    // HarvestingBuilding in their constructor; the state machine wraps the same
    // CharacterAction primitives (CharacterHarvestAction, CharacterPickUpItem,
    // CharacterStoreInFurnitureAction) directly.
    private enum CityHarvesterState
    {
        Idle,
        FindTarget,        // pick a Harvestable from a nearby Physics scan
        MoveToTarget,      // walk into the Harvestable's InteractionZone
        Harvesting,        // CharacterHarvestAction in flight
        PickupDroppedItem, // post-harvest, queue CharacterPickUpItem on a nearby WorldItem
        MoveToABStorage,   // walk into the AB's StorageFurniture interaction zone
        DepositItem,       // CharacterStoreInFurnitureAction in flight
    }

    private CityHarvesterState _cityState = CityHarvesterState.Idle;
    private Harvestable _cityTarget;
    private ItemSO _cityWantedItem;
    private StorageFurniture _cityDepositStorage;
    private bool _cityActionStarted;
    private float _cityCooldownUntil = -1f;
    private float _cityLastScanFailLogTime = -10f;

    // Reused PhysX overlap buffer (single per-instance; PhysX queries are main-thread
    // serial). 64 covers typical wilderness clutter inside a 30u scan radius.
    private const int CityHarvesterOverlapBufferSize = 64;
    private readonly Collider[] _cityOverlapBuffer = new Collider[CityHarvesterOverlapBufferSize];

    /// <summary>Wilderness search radius around the worker for Harvestable scanning.</summary>
    private const float CityHarvestScanRadius = 30f;

    /// <summary>Brief cooldown after a "no target found" tick so we don't spam PhysX queries at 0.3s × hundreds of ticks/sec.</summary>
    private const float CityHarvestScanCooldownSeconds = 2f;

    /// <summary>
    /// Exécuté chaque tick quand le worker est au travail.
    /// Utilise le GOAP planner pour décider quoi faire.
    /// </summary>
    public override void Execute()
    {
        // Plan 4b Task 7 — CityHarvester variant runs its own state machine when the
        // workplace is an AB. Pipeline contract: B2B + producer + virtual + physical-
        // harvest are all live; this branch drives the physical-harvest fallback.
        if (_workplace is AdministrativeBuilding ab)
        {
            ExecuteCityHarvesterTick(ab);
            return;
        }

        if (_workplace == null || !(_workplace is HarvestingBuilding harvesting)) return;

        // Si on a une action en cours, l'exécuter
        if (_currentAction != null)
        {
            // Vérifier que l'action est encore valide
            if (!_currentAction.IsValid(_worker))
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=orange>[JobHarvester]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification...");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=cyan>[JobHarvester]</color> {_worker.CharacterName} : action {_currentAction.ActionName} terminée.");
                _currentAction.Exit(_worker);
                _currentAction = null;

                // Forcer la replanification à chaque fois pour évaluer la capacité restante.
                // Au lieu de dépiler un plan devenu obsolète (ex: Deposit alors qu'on a encore de la place).
                _currentPlan = null;
            }
            return;
        }

        // Pas d'action en cours → Planifier
        PlanNextActions(harvesting);
    }

    /// <summary>
    /// Construit le world state actuel et lance le planner avec un nouvel objectif calculé dynamiquement.
    /// </summary>
    private void PlanNextActions(HarvestingBuilding building)
    {
        // Construire le world state
        bool hasAtLeastOneResource = false;
        var handsController = _worker.CharacterVisual?.BodyPartsController?.HandsController;
        
        var acceptedItems = building.GetAcceptedItems();
        var wantedItems = building.GetWantedItems();

        // Check 1 : Le worker a des items acceptés par le building dans son sac ?
        if (_worker.CharacterEquipment != null && _worker.CharacterEquipment.HaveInventory())
        {
            hasAtLeastOneResource = _worker.CharacterEquipment.GetInventory().HasAnyItemSO(acceptedItems);
        }

        // Check 2 : Et dans les mains ?
        if (!hasAtLeastOneResource && handsController != null && handsController.IsCarrying)
        {
            if (acceptedItems.Contains(handsController.CarriedItem.ItemSO))
            {
                hasAtLeastOneResource = true;
            }
        }

        // Check 3 : Le worker a-t-il encore de la place ?
        bool hasFreeSpace = false;
        var equip = _worker.CharacterEquipment;
        if (equip != null)
        {
            if (handsController != null && handsController.AreHandsFree())
            {
                hasFreeSpace = true;
            }
            else if (equip.HaveInventory())
            {
                // Vérifier si au moins UN wanted item peut encore rentrer dans le sac
                hasFreeSpace = equip.GetInventory().HasFreeSpaceForAnyItemSO(wantedItems);
            }
        }

        bool allResourcesHarvested = building.AreAllRequestedResourcesHarvested();
        bool needsToWork = !allResourcesHarvested;

        // Task availability scan — MOVED above hasResourcesForGoap so the "is there
        // anything actionable left?" gate can use canHarvest / looseItemExists. Without
        // that gate, a worker who just picked up wood from the only available destroy
        // target kept lying to the planner ("hasResources=false, keep gathering") even
        // when no harvest/destroy/pickup task remained; the planner could not extend any
        // chain to hasDepositedResources=true and returned a null plan, freezing the
        // worker mid-zone with wood in hand. See wiki/gotchas/harvester-deposit-freeze.md.
        bool looseItemExists = false;
        bool canHarvest = false;
        bool hasValidHarvestTasks = false;

        if (building.TaskManager != null)
        {
            looseItemExists = building.TaskManager.HasAvailableOrClaimedTask<PickupLooseItemTask>(_worker, task =>
            {
                var interactable = task.Target as WorldItem;
                return interactable != null && !_worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID());
            });

            canHarvest = building.TaskManager.HasAvailableOrClaimedTask<HarvestResourceTask>(_worker, task =>
            {
                var interactable = task.Target as Harvestable;
                if (interactable == null || _worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID())) return false;
                return interactable.HasAnyYieldOutput(building.GetWantedItems());
            });

            // Also count destroy tasks — without this, a building whose only sources of
            // wanted items are destruction-only nodes (e.g. apple trees that drop wood on
            // chop) would have hasValidHarvestTasks = false, the planner would pick Idle,
            // and workers would never start the destroy chain. The DestroyHarvestableTask's
            // own IsValid check still gates on AllowNpcDestruction so designer opt-out works.
            bool canDestroy = building.TaskManager.HasAvailableOrClaimedTask<DestroyHarvestableTask>(_worker, task =>
            {
                var interactable = task.Target as Harvestable;
                if (interactable == null || _worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID())) return false;
                if (!interactable.AllowDestruction || !interactable.AllowNpcDestruction) return false;
                return interactable.HasAnyDestructionOutput(building.GetWantedItems());
            });
            canHarvest = canHarvest || canDestroy;

            bool hasValidYieldTasks = building.TaskManager.HasAnyTaskOfType<HarvestResourceTask>(task =>
            {
                var interactable = task.Target as Harvestable;
                return interactable != null && interactable.HasAnyYieldOutput(building.GetWantedItems());
            });
            bool hasValidDestroyTasks = building.TaskManager.HasAnyTaskOfType<DestroyHarvestableTask>(task =>
            {
                var interactable = task.Target as Harvestable;
                return interactable != null
                    && interactable.AllowDestruction
                    && interactable.AllowNpcDestruction
                    && interactable.HasAnyDestructionOutput(building.GetWantedItems());
            });
            hasValidHarvestTasks = hasValidYieldTasks || hasValidDestroyTasks;
        }

        // Logique GOAP intelligente :
        // Si on a des ressources mais qu'on a ENCORE de la place, qu'une zone existe ET
        // qu'il y a du travail actionnable, on ment au planner (hasResources=false) pour
        // le forcer à continuer de Gather. Sinon (plus rien d'actionnable), on dépose ce
        // qu'on a au lieu de freezer.
        bool hasResourcesForGoap = false;

        if (hasAtLeastOneResource)
        {
            if (!hasFreeSpace)
            {
                hasResourcesForGoap = true; // Plein à craquer -> aller déposer
            }
            else
            {
                // 2026-05-19 — added (canHarvest || looseItemExists) gate. Previously the
                // worker froze after picking up the only available wood: the lie kept
                // hasResources=false, but with no destroy/harvest/pickup task left, the
                // planner could not extend any chain to hasDepositedResources=true and
                // returned a null plan, leaving the worker standing still holding wood.
                // Mirrors FarmingBuilding's worldState/IsValid symmetry canonical pattern
                // (shipped 2026-05-02). See wiki/gotchas/harvester-deposit-freeze.md.
                if (building.HasHarvestableZone && needsToWork && (canHarvest || looseItemExists))
                {
                    hasResourcesForGoap = false; // Continuer de gather
                }
                else
                {
                    hasResourcesForGoap = true; // Plus rien d'actionnable -> aller déposer ce qu'on a
                }
            }
        }

        // Reuse the scratch world-state dict (cleared + repopulated each tick — zero allocation after warm-up).
        _scratchWorldState.Clear();
        _scratchWorldState["hasHarvestZone"] = hasValidHarvestTasks; // True only if tasks exist, forces ExploreForResources
        _scratchWorldState["looseItemExists"] = looseItemExists;
        _scratchWorldState["hasResources"] = hasResourcesForGoap;
        _scratchWorldState["hasDepositedResources"] = false;
        _scratchWorldState["needsToWork"] = needsToWork;
        _scratchWorldState["isIdling"] = false;

        // Action instances are stateful (per-plan _currentTarget/_isComplete), so we must
        // create fresh ones each plan. But the LIST wrapper and the GoapGoals are stable —
        // pool those to avoid per-tick allocations.
        if (_availableActions == null) _availableActions = new List<GoapAction>(5);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_ExploreForHarvestables(building));
        _availableActions.Add(new GoapAction_HarvestResources(building));
        // Destruction-path fallback. Higher Cost (5f vs 1f for harvest) so the planner only
        // picks this when there's no harvest alternative — gated server-side on
        // Harvestable.AllowNpcDestruction (designer opt-in per node).
        _availableActions.Add(new GoapAction_DestroyHarvestable(building));
        _availableActions.Add(new GoapAction_PickupLooseItem(building));
        _availableActions.Add(new GoapAction_DepositResources(building));
        _availableActions.Add(new GoapAction_IdleInBuilding(building));

        // Cache both goals (their DesiredState dicts are constant).
        if (_cachedIdleGoal == null)
            _cachedIdleGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);
        if (_cachedHarvestAndDepositGoal == null)
            _cachedHarvestAndDepositGoal = new GoapGoal("HarvestAndDeposit", new Dictionary<string, bool> { { "hasDepositedResources", true } }, priority: 1);

        bool trulyFinishedWork = allResourcesHarvested && !hasAtLeastOneResource;
        bool stuckWaitingForTrees = building.HasHarvestableZone && !canHarvest && !looseItemExists && !hasAtLeastOneResource;

        GoapGoal targetGoal = (trulyFinishedWork || stuckWaitingForTrees)
            ? _cachedIdleGoal
            : _cachedHarvestAndDepositGoal;

        _harvestGoal = targetGoal; // On sauvegarde l'objectif courant pour l'UI de Debug

        // Pre-filter actions by IsValid(_worker) — see _scratchValidActions field comment.
        _scratchValidActions.Clear();
        for (int i = 0; i < _availableActions.Count; i++)
        {
            var a = _availableActions[i];
            if (a.IsValid(_worker)) _scratchValidActions.Add(a);
        }

        // Planifier
        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[JobHarvester]</color> {_worker.CharacterName} : nouveau plan ! Première action → {_currentAction.ActionName}");
        }
        else if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=orange>[JobHarvester]</color> {_worker.CharacterName} : impossible de planifier.");
        }
    }

    public override bool CanExecute()
    {
        // CityHarvester variant: AdministrativeBuilding accepts a JobHarvester even
        // though AB is not a HarvestingBuilding. The full Execute branch is stubbed
        // (Plan 4b Task 7) but the slot must still be assignable so an AB can hire
        // a harvester at all.
        return base.CanExecute() && (_workplace is HarvestingBuilding || _workplace is AdministrativeBuilding);
    }

    /// <summary>
    /// Plan 4b Task 7 — CityHarvester state machine. Drives the
    /// harvest → pickup → deposit cycle for materials the AB's logistics chain
    /// couldn't source through normal channels (B2B / producer / virtual).
    /// </summary>
    private void ExecuteCityHarvesterTick(AdministrativeBuilding ab)
    {
        if (_worker == null) return;

        // Defensive: clear any HarvestingBuilding-era GOAP state on first AB tick.
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
            _currentPlan = null;
        }

        switch (_cityState)
        {
            case CityHarvesterState.Idle:
                if (UnityEngine.Time.unscaledTime < _cityCooldownUntil) return;
                _cityWantedItem = PickWantedItem(ab);
                if (_cityWantedItem == null)
                {
                    // Queue empty — stay idle.
                    return;
                }
                _cityState = CityHarvesterState.FindTarget;
                break;

            case CityHarvesterState.FindTarget:
                if (_cityWantedItem == null)
                {
                    ResetCityState();
                    return;
                }
                _cityTarget = FindHarvestableYielding(_worker, _cityWantedItem);
                if (_cityTarget == null)
                {
                    // Nothing visible. Cool down and re-check on the next eligible tick.
                    LogScanFailThrottled(_cityWantedItem, ab);
                    _cityCooldownUntil = UnityEngine.Time.unscaledTime + CityHarvestScanCooldownSeconds;
                    ResetCityState();
                    return;
                }
                _cityState = CityHarvesterState.MoveToTarget;
                break;

            case CityHarvesterState.MoveToTarget:
                if (_cityTarget == null || !_cityTarget.CanHarvest())
                {
                    ResetCityState();
                    return;
                }
                if (CityIsAtTarget(_worker, _cityTarget))
                {
                    _worker.CharacterMovement?.ResetPath();
                    _cityState = CityHarvesterState.Harvesting;
                    _cityActionStarted = false;
                    break;
                }
                // Re-fire SetDestination on path loss (rule #36 anti-freeze pattern).
                var movement = _worker.CharacterMovement;
                if (movement != null && !movement.HasPath)
                {
                    Vector3 dest = _cityTarget.transform.position;
                    if (_cityTarget.InteractionZone != null)
                    {
                        dest = _cityTarget.InteractionZone.bounds.ClosestPoint(_worker.transform.position);
                    }
                    movement.SetDestination(dest);
                }
                break;

            case CityHarvesterState.Harvesting:
                if (_cityTarget == null || !_cityTarget.CanHarvest())
                {
                    ResetCityState();
                    return;
                }
                if (!_cityActionStarted)
                {
                    var harvestAction = new CharacterHarvestAction(_worker, _cityTarget);
                    if (_worker.CharacterActions.ExecuteAction(harvestAction))
                    {
                        _cityActionStarted = true;
                        // Cache the harvestable's spawn-anchor (worker position+forward) so
                        // we can scan for the dropped WorldItem after the animation.
                        harvestAction.OnActionFinished += () =>
                        {
                            _cityState = CityHarvesterState.PickupDroppedItem;
                            _cityActionStarted = false;
                            if (NPCDebug.VerboseJobs)
                            {
                                Debug.Log($"<color=cyan>[JobHarvester/City]</color> {_worker.CharacterName} harvested '{_cityTarget?.gameObject.name}' for {_cityWantedItem.ItemName}.");
                            }
                        };
                    }
                    else
                    {
                        // Action rejected — clear and retry.
                        if (_worker.CharacterActions.CurrentAction != null)
                            _worker.CharacterActions.ClearCurrentAction();
                        ResetCityState();
                    }
                }
                break;

            case CityHarvesterState.PickupDroppedItem:
                // CharacterHarvestAction spawns the WorldItem at the worker's position +
                // forward * 0.5 (see CharacterActions.ApplyHarvestOnServer). Scan a small
                // radius around the worker for a matching loose item.
                if (!_cityActionStarted)
                {
                    WorldItem dropped = FindNearbyDroppedItem(_worker, _cityWantedItem);
                    if (dropped == null)
                    {
                        // Nothing matching nearby (rare race — another worker grabbed it,
                        // or the harvestable yielded something else). Skip to deposit if
                        // we already happen to be carrying the wanted item; otherwise reset.
                        var carried = GetCarriedItem(_worker);
                        if (carried != null && carried.ItemSO == _cityWantedItem)
                        {
                            _cityState = CityHarvesterState.MoveToABStorage;
                            _cityDepositStorage = null;
                            return;
                        }
                        ResetCityState();
                        return;
                    }

                    var instance = dropped.ItemInstance;
                    if (instance == null)
                    {
                        ResetCityState();
                        return;
                    }
                    var pickupAction = new CharacterPickUpItem(_worker, instance, dropped.gameObject);
                    if (_worker.CharacterActions.ExecuteAction(pickupAction))
                    {
                        _cityActionStarted = true;
                        pickupAction.OnActionFinished += () =>
                        {
                            _cityState = CityHarvesterState.MoveToABStorage;
                            _cityDepositStorage = null;
                            _cityActionStarted = false;
                        };
                    }
                    else
                    {
                        if (_worker.CharacterActions.CurrentAction != null)
                            _worker.CharacterActions.ClearCurrentAction();
                        ResetCityState();
                    }
                }
                break;

            case CityHarvesterState.MoveToABStorage:
                // Find a StorageFurniture in the AB that has free space for the carried item.
                var carriedNow = GetCarriedItem(_worker);
                if (carriedNow == null)
                {
                    ResetCityState();
                    return;
                }
                if (_cityDepositStorage == null)
                {
                    _cityDepositStorage = ab.FindStorageFurnitureForItem(carriedNow);
                    if (_cityDepositStorage == null)
                    {
                        // No storage furniture has space. Drop in the building zone and let
                        // the LogisticsManager sort it out via GoapAction_GatherStorageItems.
                        ab.AddToInventory(carriedNow); // logical add (deposit-by-fiat)
                        ab.DecrementUnfulfillableMaterial(_cityWantedItem, 1);
                        // Remove from worker's inventory or hands.
                        DropFromWorker(_worker, carriedNow);
                        ResetCityState();
                        return;
                    }
                }
                if (CityIsAtStorage(_worker, _cityDepositStorage))
                {
                    _worker.CharacterMovement?.ResetPath();
                    _cityState = CityHarvesterState.DepositItem;
                    _cityActionStarted = false;
                    break;
                }
                var moveMgmt = _worker.CharacterMovement;
                if (moveMgmt != null && !moveMgmt.HasPath)
                {
                    moveMgmt.SetDestination(_cityDepositStorage.GetInteractionPosition(_worker.transform.position));
                }
                break;

            case CityHarvesterState.DepositItem:
                if (!_cityActionStarted)
                {
                    var carriedForDeposit = GetCarriedItem(_worker);
                    if (carriedForDeposit == null || _cityDepositStorage == null)
                    {
                        ResetCityState();
                        return;
                    }
                    // Re-validate storage has space (another worker may have filled it
                    // while we walked).
                    if (_cityDepositStorage.IsLocked || !_cityDepositStorage.HasFreeSpaceForItem(carriedForDeposit))
                    {
                        // Try to repick on next tick.
                        _cityDepositStorage = null;
                        _cityState = CityHarvesterState.MoveToABStorage;
                        return;
                    }
                    var storeAction = new CharacterStoreInFurnitureAction(_worker, carriedForDeposit, _cityDepositStorage);
                    if (_worker.CharacterActions.ExecuteAction(storeAction))
                    {
                        _cityActionStarted = true;
                        var wantedSnapshot = _cityWantedItem;
                        storeAction.OnActionFinished += () =>
                        {
                            ab.AddToInventory(carriedForDeposit);
                            ab.DecrementUnfulfillableMaterial(wantedSnapshot, 1);
                            if (NPCDebug.VerboseJobs)
                            {
                                Debug.Log($"<color=green>[JobHarvester/City]</color> {_worker.CharacterName} deposited {carriedForDeposit.ItemSO.ItemName} into {_cityDepositStorage.FurnitureName} for AB '{ab.BuildingName}'.");
                            }
                            ResetCityState();
                        };
                    }
                    else
                    {
                        if (_worker.CharacterActions.CurrentAction != null)
                            _worker.CharacterActions.ClearCurrentAction();
                        ResetCityState();
                    }
                }
                break;
        }
    }

    private void ResetCityState()
    {
        _cityState = CityHarvesterState.Idle;
        _cityTarget = null;
        _cityWantedItem = null;
        _cityDepositStorage = null;
        _cityActionStarted = false;
    }

    /// <summary>Picks the first non-empty queue entry's item. FIFO; simple for v1.</summary>
    private static ItemSO PickWantedItem(AdministrativeBuilding ab)
    {
        var queue = ab.GetUnfulfillableHarvestQueue();
        if (queue == null || queue.Count == 0) return null;
        for (int i = 0; i < queue.Count; i++)
        {
            var entry = queue[i];
            if (entry != null && entry.Item != null && entry.Qty > 0) return entry.Item;
        }
        return null;
    }

    /// <summary>
    /// Physics.OverlapSphereNonAlloc scan around the worker for a <see cref="Harvestable"/>
    /// that yields <paramref name="wanted"/>. Skips depleted / blacklisted targets and
    /// returns the closest match. Allocation-free (uses the per-instance overlap buffer).
    /// </summary>
    private Harvestable FindHarvestableYielding(Character worker, ItemSO wanted)
    {
        if (worker == null || wanted == null) return null;

        int hits = Physics.OverlapSphereNonAlloc(
            worker.transform.position,
            CityHarvestScanRadius,
            _cityOverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        Harvestable best = null;
        float bestDistSqr = float.MaxValue;
        for (int i = 0; i < hits; i++)
        {
            var col = _cityOverlapBuffer[i];
            if (col == null) continue;
            var h = col.GetComponentInParent<Harvestable>();
            if (h == null) continue;
            if (!h.CanHarvest()) continue;
            if (worker.PathingMemory != null && worker.PathingMemory.IsBlacklisted(h.gameObject.GetInstanceID())) continue;
            if (!h.HasYieldOutput(wanted)) continue;

            float dSqr = (h.transform.position - worker.transform.position).sqrMagnitude;
            if (dSqr < bestDistSqr)
            {
                best = h;
                bestDistSqr = dSqr;
            }
        }
        return best;
    }

    /// <summary>
    /// Scans a tight radius around the worker for a <see cref="WorldItem"/> matching
    /// <paramref name="wanted"/>. The harvest spawn anchor is `worker.position + forward * 0.5`
    /// per <see cref="CharacterActions.ApplyHarvestOnServer"/>, so a 2 m radius is plenty.
    /// </summary>
    private WorldItem FindNearbyDroppedItem(Character worker, ItemSO wanted)
    {
        if (worker == null || wanted == null) return null;

        int hits = Physics.OverlapSphereNonAlloc(
            worker.transform.position,
            2f,
            _cityOverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        WorldItem nearest = null;
        float nearestDistSqr = float.MaxValue;
        for (int i = 0; i < hits; i++)
        {
            var col = _cityOverlapBuffer[i];
            if (col == null) continue;
            var wi = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (wi == null || wi.IsBeingCarried) continue;
            if (wi.ItemInstance == null || wi.ItemInstance.ItemSO != wanted) continue;

            float dSqr = (wi.transform.position - worker.transform.position).sqrMagnitude;
            if (dSqr < nearestDistSqr)
            {
                nearest = wi;
                nearestDistSqr = dSqr;
            }
        }
        return nearest;
    }

    private static bool CityIsAtTarget(Character worker, Harvestable target)
    {
        if (worker == null || target == null) return false;
        if (target.InteractionZone != null)
        {
            if (target.InteractionZone.bounds.Contains(worker.transform.position)) return true;
            float dist = Vector3.Distance(
                worker.transform.position,
                target.InteractionZone.bounds.ClosestPoint(worker.transform.position));
            return dist <= 2.5f; // matches CharacterHarvestAction.CanExecute fallback
        }
        return Vector3.Distance(worker.transform.position, target.transform.position) <= 3f;
    }

    private static bool CityIsAtStorage(Character worker, StorageFurniture storage)
    {
        if (worker == null || storage == null) return false;
        var interactable = storage.GetComponent<InteractableObject>();
        if (interactable != null && interactable.InteractionZone != null)
        {
            if (interactable.IsCharacterInInteractionZone(worker)) return true;
            // Softlock guard — path exhausted within 2f flat-XZ.
            var movement = worker.CharacterMovement;
            bool pathExhausted = movement != null
                && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f);
            if (pathExhausted)
            {
                Vector3 ip = storage.GetInteractionPosition(worker.transform.position);
                Vector3 wp = worker.transform.position;
                Vector3 a = new Vector3(wp.x, 0f, wp.z);
                Vector3 b = new Vector3(ip.x, 0f, ip.z);
                return Vector3.Distance(a, b) <= 2f;
            }
            return false;
        }
        return Vector3.Distance(worker.transform.position, storage.GetInteractionPosition(worker.transform.position)) <= 1.5f;
    }

    private static ItemInstance GetCarriedItem(Character worker)
    {
        if (worker == null) return null;
        var inventory = worker.CharacterEquipment?.GetInventory();
        if (inventory != null && inventory.ItemSlots.Exists(s => !s.IsEmpty()))
        {
            return inventory.ItemSlots.FindLast(s => !s.IsEmpty()).ItemInstance;
        }
        return worker.CharacterVisual?.BodyPartsController?.HandsController?.CarriedItem;
    }

    private static void DropFromWorker(Character worker, ItemInstance item)
    {
        if (worker == null || item == null) return;
        var inventory = worker.CharacterEquipment?.GetInventory();
        if (inventory != null && inventory.HasAnyItemSO(new List<ItemSO> { item.ItemSO }))
        {
            inventory.RemoveItem(item, worker);
            return;
        }
        worker.CharacterVisual?.BodyPartsController?.HandsController?.DropCarriedItem();
    }

    private void LogScanFailThrottled(ItemSO wanted, AdministrativeBuilding ab)
    {
        if (!NPCDebug.VerboseJobs) return;
        float now = UnityEngine.Time.unscaledTime;
        if (now - _cityLastScanFailLogTime < 5f) return;
        _cityLastScanFailLogTime = now;
        Debug.Log($"<color=#ff8866>[JobHarvester/City]</color> {_worker?.CharacterName} at {ab.BuildingName}: no Harvestable yielding {wanted.ItemName} within {CityHarvestScanRadius}u.");
    }

    /// <summary>
    /// Spécifie si le harvester a encore de la récolte ou du dépôt à faire.
    /// Renvoie Faux s'il n'a plus rien sur lui et que le batiment n'a plus besoin de rien.
    /// </summary>
    public override bool HasWorkToDo()
    {
        if (_workplace is not HarvestingBuilding building) return false;

        bool hasAtLeastOneResource = _worker.CharacterEquipment != null && 
                                     _worker.CharacterEquipment.HaveInventory() && 
                                     _worker.CharacterEquipment.GetInventory().ItemSlots.Any(slot => !slot.IsEmpty());
                                     
        if (_worker.CharacterVisual?.BodyPartsController?.HandsController != null)
        {
            if (_worker.CharacterVisual.BodyPartsController.HandsController.IsCarrying)
            {
                hasAtLeastOneResource = true;
            }
        }

        bool allResourcesHarvested = building.AreAllRequestedResourcesHarvested();

        // Le travail est fini si : 
        // 1. On n'a plus rien sur nous pour déposer
        // 2. Le batiment a toutes les ressources voulues OU il n'y a plus de zone avec ressources
        if (allResourcesHarvested && !hasAtLeastOneResource)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Les harvesters commencent tôt le matin.
    /// </summary>
    public override List<ScheduleEntry> GetWorkSchedule()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry(6, 16, ScheduleActivity.Work, 10)
        };
    }

    /// <summary>
    /// Override Assign pour ajouter l'employé au building.
    /// </summary>
    public override void Assign(Character worker, CommercialBuilding workplace)
    {
        base.Assign(worker, workplace);

        if (workplace is HarvestingBuilding harvesting)
        {
            harvesting.AddEmployee(worker);
        }
    }

    /// <summary>
    /// Override Unassign pour retirer l'employé du building.
    /// </summary>
    public override void Unassign()
    {
        if (_workplace is HarvestingBuilding harvesting && _worker != null)
        {
            harvesting.RemoveEmployee(_worker);
        }

        // Cleanup GOAP
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;

        base.Unassign();
    }
}
