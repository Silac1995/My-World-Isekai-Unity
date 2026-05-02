using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Farming;
using MWI.WorldSystem;

/// <summary>
/// JobFarmer: plants, waters, and harvests crops in a <see cref="FarmingBuilding"/>. GOAP-driven;
/// mirrors <see cref="JobHarvester"/>'s shape exactly (cached goals, scratch worldState dict,
/// fresh action instances per plan, ExecuteIntervalSeconds=0.3f).
///
/// Goal priority (highest first):
///   HarvestMatureCells   → HarvestResources → DepositResources
///   WaterDryCells        → FetchTool(WateringCan) → WaterCrop → ReturnTool(WateringCan)
///   PlantEmptyCells      → FetchSeed → PlantCrop
///   DepositResources     → DepositResources (when bag full / carried produce)
///   Idle                 → IdleInBuilding
///
/// Watering can chain reuses Plan 1's <see cref="GoapAction_FetchToolFromStorage"/> /
/// <see cref="GoapAction_ReturnToolToStorage"/> with the building's inherited tool storage as
/// source. Per-task pattern: fetch can on demand, return after each WaterCrop.
///
/// Schedule 6h-18h (slightly later than Harvester's 6-16h to allow evening watering passes).
/// </summary>
[System.Serializable]
public class JobFarmer : Job
{
    [SerializeField] private string _jobTitle;
    [SerializeField] private JobType _jobType;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Harvester;
    public override JobType Type => _jobType;

    // Heavy-planning job: GOAP plan + per-tick task scans against the building's
    // BuildingTaskManager. Mirrors JobHarvester's 3.3 Hz. See
    // wiki/projects/optimisation-backlog.md entry #2.
    public override float ExecuteIntervalSeconds => 0.3f;

    // GOAP
    private GoapGoal _currentGoal;
    private List<GoapAction> _availableActions;
    // Pre-filtered subset of _availableActions where IsValid(worker) is true at planning time.
    // GoapPlanner.Plan does NOT call IsValid — see JobHarvester._scratchValidActions for the full
    // rationale (without this filter, identical-prec/effect actions with different Cost cause
    // infinite-replan loops).
    private List<GoapAction> _scratchValidActions = new List<GoapAction>(8);
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    // Per-tick allocation pool — clear-and-reuse the worldState dict; cache the goals.
    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(16);
    private GoapGoal _cachedHarvestGoal;
    private GoapGoal _cachedWaterGoal;
    private GoapGoal _cachedPlantGoal;
    private GoapGoal _cachedDepositGoal;
    private GoapGoal _cachedIdleGoal;

    // Throttle for the on-shift-but-Idle diagnostic dump (1 Hz per worker).
    private float _lastIdleDumpTime = -10f;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _currentGoal != null ? _currentGoal.GoalName : "No Goal";

    public JobFarmer(string jobTitle = "Farmer", JobType jobType = JobType.Farmer)
    {
        _jobTitle = jobTitle;
        _jobType = jobType;
    }

    public override void Execute()
    {
        if (_workplace == null || !(_workplace is FarmingBuilding farm)) return;

        // Tick the in-flight action (validity → execute → completion check).
        if (_currentAction != null)
        {
            if (!_currentAction.IsValid(_worker))
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=orange>[JobFarmer]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalid; replanning.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=cyan>[JobFarmer]</color> {_worker.CharacterName} : action {_currentAction.ActionName} complete.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                // Force replan on every action completion — same rationale as JobHarvester:
                // pop-the-plan would risk executing a stale next action (e.g. Deposit when we
                // can still pick more up).
                _currentPlan = null;
            }
            return;
        }

        // No action in flight → plan.
        PlanNextActions(farm);
    }

    /// <summary>
    /// Builds the world-state dict, picks the highest-priority achievable goal, runs
    /// <see cref="GoapPlanner.Plan"/>, and dequeues the first action.
    /// </summary>
    private void PlanNextActions(FarmingBuilding farm)
    {
        // Reactive re-scan (throttled to 1 Hz per building). PlantScan + WaterScan otherwise
        // only run at Start + OnNewDay — so seeds dropped into a chest during a shift would
        // stay invisible to the worldState below until next midnight. Calling here makes
        // them visible within ~1s, regardless of who delivered them (player drop, NPC
        // logistics inbound, debug spawn).
        farm.RefreshScansThrottled();

        // ── Build worldState ──────────────────────────────────────────

        // Use HasAvailableOrClaimedTask: tasks auto-claimed onto _worker by the
        // quest-system auto-claim path (CommercialBuilding.TryAutoClaimExistingQuests on
        // WorkerStartingShift + the OnQuestPublished subscriber for tasks registered
        // mid-shift) move from _availableTasks → _inProgressTasks the moment they're
        // registered. The plain HasAvailableTask helper only walked _availableTasks, so
        // PlantScan registering 25 tasks → all 25 instantly auto-claimed → worldState
        // saw 0 → planner picked Idle. Walking both buckets restores the intended
        // semantic of "is there any plant work I can take or already own?".
        bool hasUnfilledHarvestTask = farm.TaskManager != null && farm.TaskManager.HasAvailableOrClaimedTask<HarvestResourceTask>(_worker);
        bool hasUnfilledWaterTask = farm.TaskManager != null && farm.TaskManager.HasAvailableOrClaimedTask<WaterCropTask>(_worker);
        bool hasUnfilledPlantTask = farm.TaskManager != null && farm.TaskManager.HasAvailableOrClaimedTask<PlantCropTask>(_worker);

        var hands = _worker.CharacterVisual?.BodyPartsController?.HandsController;
        var inventory = _worker.CharacterEquipment != null && _worker.CharacterEquipment.HaveInventory()
            ? _worker.CharacterEquipment.GetInventory()
            : null;

        bool handsCarrying = hands != null && hands.IsCarrying && hands.CarriedItem != null;
        bool hasSeedInHand = handsCarrying && hands.CarriedItem.ItemSO is SeedSO;
        bool hasCanInHand = handsCarrying && farm.WateringCanItem != null
                            && hands.CarriedItem.ItemSO == farm.WateringCanItem;

        bool hasMatchingSeedInStorage = farm.HasAnySeedForActionablePlantTask(_worker);

        // Loose item presence drives the PickupLooseItem step in the harvest chain. Mirrors
        // JobHarvester's computation (line 174-184): a loose item "exists" iff there's a
        // PickupLooseItemTask whose target WorldItem is reachable for this worker (filter
        // blacklisted instance IDs the same way). Without this, JobFarmer's worldState had
        // looseItemExists=false hard-coded → PickupLooseItem's precondition always failed
        // post-Harvest → workers harvested but never picked up the dropped item, then the
        // planner re-picked Harvest because hasUnfilledHarvestTask was still true.
        bool looseItemExists = false;
        if (farm.TaskManager != null)
        {
            looseItemExists = farm.TaskManager.HasAvailableOrClaimedTask<PickupLooseItemTask>(_worker, task =>
            {
                var interactable = task.Target as WorldItem;
                return interactable != null && !_worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID());
            });
        }
        bool hasWateringCanAvailable = farm.WateringCanItem != null
                                        && farm.HasToolStorage
                                        && farm.ToolStorage != null
                                        && StorageHasItem(farm.ToolStorage, farm.WateringCanItem);

        // Anything in the bag counts as deposit-able. Hands-held items also count UNLESS they're
        // a seed (still planting) or the watering can (still mid-water-cycle).
        bool hasResourcesToDeposit = false;
        if (inventory != null && inventory.ItemSlots != null)
        {
            for (int i = 0; i < inventory.ItemSlots.Count; i++)
            {
                var slot = inventory.ItemSlots[i];
                if (slot != null && !slot.IsEmpty()) { hasResourcesToDeposit = true; break; }
            }
        }
        if (!hasResourcesToDeposit && handsCarrying && !hasSeedInHand && !hasCanInHand)
            hasResourcesToDeposit = true;

        _scratchWorldState.Clear();
        _scratchWorldState["hasUnfilledHarvestTask"] = hasUnfilledHarvestTask;
        _scratchWorldState["hasUnfilledWaterTask"] = hasUnfilledWaterTask;
        _scratchWorldState["hasUnfilledPlantTask"] = hasUnfilledPlantTask;
        _scratchWorldState["hasSeedInHand"] = hasSeedInHand;
        _scratchWorldState["hasMatchingSeedInStorage"] = hasMatchingSeedInStorage;
        _scratchWorldState["hasResources"] = hasResourcesToDeposit;
        _scratchWorldState["hasDepositedResources"] = false;
        _scratchWorldState["hasPlantedCrop"] = false;
        _scratchWorldState["hasWateredCell"] = false;
        _scratchWorldState["isIdling"] = false;
        // hasHarvestZone is REQUIRED by GoapAction_HarvestResources's precondition. JobHarvester
        // sets the same key from its own scan results; without it here, the planner sees the
        // default-missing key as false (GoapAction.ArePreconditionsMet treats missing-as-false)
        // and HarvestResources can never apply, so the HarvestMatureCells goal can't form a plan.
        // Symptom: 'NPCs do not do anything, even though their goap says HarvestMatureCells.'
        // Mirroring hasUnfilledHarvestTask is the right value: the zone is "harvestable" iff
        // there's at least one valid HarvestResourceTask.
        _scratchWorldState["hasHarvestZone"] = hasUnfilledHarvestTask;
        // looseItemExists is now a LIVE value (see computation above). When a loose
        // PickupLooseItemTask exists for this worker, the planner picks PickupLooseItem
        // BEFORE attempting another HarvestResources (PickupLooseItem.Cost=0.5 vs
        // HarvestResources.Cost=1.0 also nudges this). Without the live value, workers
        // harvested but never picked up the drop.
        _scratchWorldState["looseItemExists"] = looseItemExists;

        // Tool keys use the WateringCan ItemSO's name (Plan 1 convention — see
        // GoapAction_FetchToolFromStorage / ReturnToolToStorage for the matching reads).
        string canKey = farm.WateringCanItem != null ? farm.WateringCanItem.name : "null";
        _scratchWorldState[$"hasToolInHand_{canKey}"] = hasCanInHand;
        _scratchWorldState[$"toolNeededForTask_{canKey}"] = hasUnfilledWaterTask;
        _scratchWorldState[$"taskCompleteForTool_{canKey}"] = hasCanInHand && !hasUnfilledWaterTask;
        _scratchWorldState[$"toolReturned_{canKey}"] = false;

        // ── Build action library (fresh instances per plan — actions are stateful) ──

        if (_availableActions == null) _availableActions = new List<GoapAction>(10);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_HarvestResources(farm));
        // Bridge between HarvestResources (effects: looseItemExists=true) and
        // DepositResources (preconditions: hasResources=true). Without this action the
        // planner cannot satisfy DepositResources's precondition because no other action
        // sets hasResources=true. Mirrors JobHarvester's action library — same harvest →
        // pickup → deposit chain.
        _availableActions.Add(new GoapAction_PickupLooseItem(farm));
        _availableActions.Add(new GoapAction_DepositResources(farm));
        _availableActions.Add(new GoapAction_FetchSeed(farm));
        _availableActions.Add(new GoapAction_PlantCrop(farm));
        if (farm.WateringCanItem != null && farm.HasToolStorage)
        {
            _availableActions.Add(new GoapAction_FetchToolFromStorage(farm, farm.WateringCanItem));
            _availableActions.Add(new GoapAction_WaterCrop(farm));
            _availableActions.Add(new GoapAction_ReturnToolToStorage(farm, farm.WateringCanItem));
        }
        _availableActions.Add(new GoapAction_IdleInBuilding(farm));

        // ── Cache goals (their DesiredState dicts are constant across ticks). ──

        if (_cachedHarvestGoal == null)
            _cachedHarvestGoal = new GoapGoal("HarvestMatureCells",
                new Dictionary<string, bool> { { "hasDepositedResources", true } }, priority: 5);
        if (_cachedWaterGoal == null)
            _cachedWaterGoal = new GoapGoal("WaterDryCells",
                new Dictionary<string, bool> { { $"toolReturned_{canKey}", true } }, priority: 4);
        if (_cachedPlantGoal == null)
            _cachedPlantGoal = new GoapGoal("PlantEmptyCells",
                new Dictionary<string, bool> { { "hasPlantedCrop", true } }, priority: 3);
        if (_cachedDepositGoal == null)
            _cachedDepositGoal = new GoapGoal("DepositResources",
                new Dictionary<string, bool> { { "hasDepositedResources", true } }, priority: 2);
        if (_cachedIdleGoal == null)
            _cachedIdleGoal = new GoapGoal("Idle",
                new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);

        // ── Pick highest-priority goal whose plan is achievable ──
        // Note: Deposit moves above Water/Plant when bag is full so the farmer always offloads
        // before chaining another fetch (avoids carrying 5 items + a watering can for 30s).

        GoapGoal targetGoal;
        // Single-goal funnel for everything that ends in hasDepositedResources=true:
        //   (a) mature trees to harvest (Harvest → Pickup → Deposit),
        //   (b) loose items dropped on the ground from a previous harvest (Pickup → Deposit),
        //   (c) resources already in the worker's bag/hand (Deposit alone).
        // All three converge on the HarvestMatureCells goal (terminal hasDeposited
        // Resources=true). Previously the cascade gated this on hasUnfilledHarvestTask
        // ALONE → after a worker depleted every tree and items lay on the ground, the
        // cascade fell through to PlantEmptyCells (no path because no seed in hand) and
        // the worker froze. Symptom: 'She harvested a crop, then as soon as her action
        // ended, her goal went to PlantEmptyCells and she just stopped moving.' Adding
        // looseItemExists + hasResourcesToDeposit to the trigger covers all three cases.
        if (hasUnfilledHarvestTask || looseItemExists || hasResourcesToDeposit) targetGoal = _cachedHarvestGoal;
        // "Use what you're already carrying" — when hands hold a seed and a plant task
        // exists, plant FIRST regardless of water-task priority. Without this rule, a
        // worker who fetched a seed on a previous tick can get the goal flipped to
        // WaterDryCells the moment new water tasks register; FetchTool (needs hands
        // free) is filtered out → no plan → Planning / Idle forever, frozen with the
        // seed in hand. Symptom seen by user: 'NPCs want to water crop but they have a
        // seed in their hands. They would move around the storage and not do anything.'
        else if (hasSeedInHand && hasUnfilledPlantTask) targetGoal = _cachedPlantGoal;
        // WaterDryCells (terminal state: toolReturned_{canKey}=true) fires when EITHER:
        //   (a) there is at least one water task AND we have a path to a can (in-hand or in
        //       tool storage), OR
        //   (b) we are already carrying the can — no matter whether water tasks remain. This
        //       is the "finished watering, now put the can back" case. Without this branch,
        //       the cascade falls through Harvest/Deposit/Plant (all rejected because hands
        //       are occupied with a non-deposit-able can) and lands on Idle, leaving the
        //       worker holding the can forever.
        else if ((hasUnfilledWaterTask && (hasCanInHand || hasWateringCanAvailable)) || hasCanInHand) targetGoal = _cachedWaterGoal;
        else if (hasUnfilledPlantTask && (hasSeedInHand || hasMatchingSeedInStorage)) targetGoal = _cachedPlantGoal;
        else targetGoal = _cachedIdleGoal;

        // Diagnostic: when a Farmer who is on shift falls through to Idle, dump the worldState
        // so we can see WHY (which precondition is false). Throttled to once per second per
        // worker to avoid log spam at the BT tick rate.
        if (targetGoal == _cachedIdleGoal && _workplace != null && _workplace.IsWorkerOnShift(_worker))
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastIdleDumpTime > 1f)
            {
                _lastIdleDumpTime = now;

                // Deep task-state probe: does THIS farm's TaskManager actually hold any
                // PlantCropTasks claimed by THIS worker? Mismatches here mean either (a) the
                // building Nyx is hired at is NOT the same FarmingBuilding instance shown by
                // the building debug UI, or (b) auto-claim added a different Character
                // reference than _worker (save/load drift, NPC despawn-respawn, etc.).
                int availPlant = 0, inProgressPlant = 0, inProgressClaimedByMe = 0;
                int availHarvest = 0, availHarvestCanHarvestNow = 0;
                if (farm.TaskManager != null)
                {
                    var av = farm.TaskManager.AvailableTasks;
                    for (int i = 0; i < av.Count; i++)
                    {
                        if (av[i] is PlantCropTask) availPlant++;
                        if (av[i] is HarvestResourceTask hrt)
                        {
                            availHarvest++;
                            // Mirror HarvestResourceTask.IsValid → target.CanHarvest. If 0 of N
                            // can harvest, every task is filtered out by HasAvailableOrClaimed
                            // Task and hasUnfilledHarvestTask=False even though there's a
                            // long Available list — usually because crops aren't mature yet.
                            var tgt = hrt.Target as Harvestable;
                            if (tgt != null && tgt.CanHarvest()) availHarvestCanHarvestNow++;
                        }
                    }
                    var ip = farm.TaskManager.InProgressTasks;
                    for (int i = 0; i < ip.Count; i++)
                    {
                        if (!(ip[i] is PlantCropTask pct)) continue;
                        inProgressPlant++;
                        if (pct.ClaimedByWorkers.Contains(_worker)) inProgressClaimedByMe++;
                    }
                }
                int buildingInvCount = 0;
                if (_workplace != null && _workplace.Inventory != null) buildingInvCount = _workplace.Inventory.Count;

                Debug.Log(
                    $"<color=orange>[JobFarmer]</color> {_worker.CharacterName} (instanceID={_worker.GetInstanceID()}) planning Idle at " +
                    $"{farm.BuildingName} (instanceID={farm.GetInstanceID()}, Tasks: avail.Plant={availPlant}, inProgress.Plant={inProgressPlant}, " +
                    $"inProgress.Plant claimed-by-me={inProgressClaimedByMe}, " +
                    $"avail.Harvest={availHarvest} (canHarvestNow={availHarvestCanHarvestNow}), " +
                    $"building.Inventory.Count={buildingInvCount}). worldState: " +
                    $"hasUnfilledHarvestTask={hasUnfilledHarvestTask}, hasUnfilledWaterTask={hasUnfilledWaterTask}, hasUnfilledPlantTask={hasUnfilledPlantTask}, " +
                    $"hasSeedInHand={hasSeedInHand}, hasMatchingSeedInStorage={hasMatchingSeedInStorage}, " +
                    $"hasCanInHand={hasCanInHand}, hasWateringCanAvailable={hasWateringCanAvailable}, " +
                    $"hasResourcesToDeposit={hasResourcesToDeposit}.");
            }
        }

        _currentGoal = targetGoal;

        // ── Pre-filter actions by IsValid (planner does NOT call IsValid) ──
        _scratchValidActions.Clear();
        for (int i = 0; i < _availableActions.Count; i++)
        {
            var a = _availableActions[i];
            if (a.IsValid(_worker)) _scratchValidActions.Add(a);
        }

        // ── Plan ──
        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[JobFarmer]</color> {_worker.CharacterName} : new plan ({_currentGoal.GoalName}); first action → {_currentAction.ActionName}");
        }
        else
        {
            // Plan failed for a non-Idle goal — surface the same deep diagnostic dump used
            // for the Idle path. Without this, a worker stuck on goal=HarvestMatureCells with
            // action=Planning/Idle (because the planner couldn't chain the actions in
            // _scratchValidActions) was invisible in the console.
            if (targetGoal != _cachedIdleGoal && _workplace != null && _workplace.IsWorkerOnShift(_worker))
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now - _lastIdleDumpTime > 1f)
                {
                    _lastIdleDumpTime = now;

                    // Inventory snapshot — what is the worker actually carrying?
                    var hands3 = _worker.CharacterVisual?.BodyPartsController?.HandsController;
                    string carriedName = hands3?.CarriedItem?.ItemSO?.ItemName ?? "<none>";
                    int bagItems = 0;
                    if (inventory != null && inventory.ItemSlots != null)
                        for (int i = 0; i < inventory.ItemSlots.Count; i++)
                            if (inventory.ItemSlots[i] != null && !inventory.ItemSlots[i].IsEmpty()) bagItems++;

                    // Which actions made it through the IsValid pre-filter?
                    var validActionNames = new System.Text.StringBuilder();
                    for (int i = 0; i < _scratchValidActions.Count; i++)
                    {
                        if (i > 0) validActionNames.Append(",");
                        validActionNames.Append(_scratchValidActions[i].ActionName);
                    }

                    Debug.Log(
                        $"<color=red>[JobFarmer]</color> {_worker.CharacterName} (instanceID={_worker.GetInstanceID()}) " +
                        $"NO-PLAN for goal '{targetGoal.GoalName}' at {farm.BuildingName}. " +
                        $"Carrying='{carriedName}', bagItems={bagItems}, " +
                        $"validActions=[{validActionNames}]. worldState: " +
                        $"hasUnfilledHarvestTask={hasUnfilledHarvestTask}, hasUnfilledWaterTask={hasUnfilledWaterTask}, hasUnfilledPlantTask={hasUnfilledPlantTask}, " +
                        $"hasSeedInHand={hasSeedInHand}, hasMatchingSeedInStorage={hasMatchingSeedInStorage}, " +
                        $"hasCanInHand={hasCanInHand}, hasWateringCanAvailable={hasWateringCanAvailable}, " +
                        $"hasResourcesToDeposit={hasResourcesToDeposit}, looseItemExists={looseItemExists}, hasHarvestZone={hasUnfilledHarvestTask}.");
                }
            }
            else if (NPCDebug.VerboseJobs)
            {
                Debug.Log($"<color=orange>[JobFarmer]</color> {_worker.CharacterName} : no plan for goal {_currentGoal?.GoalName ?? "?"}.");
            }
        }
    }

    /// <summary>True if the building's TaskManager has at least one unclaimed task of type T.</summary>
    private static bool HasAvailableTask<T>(FarmingBuilding farm) where T : BuildingTask
    {
        if (farm == null || farm.TaskManager == null) return false;
        var tasks = farm.TaskManager.AvailableTasks;
        for (int i = 0; i < tasks.Count; i++)
        {
            if (tasks[i] is T) return true;
        }
        return false;
    }

    /// <summary>True if any non-empty slot in <paramref name="storage"/> holds <paramref name="item"/>.</summary>
    private static bool StorageHasItem(StorageFurniture storage, ItemSO item)
    {
        if (storage == null || item == null) return false;
        var slots = storage.ItemSlots;
        if (slots == null) return false;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == item) return true;
        }
        return false;
    }

    public override bool CanExecute() => base.CanExecute() && _workplace is FarmingBuilding;

    public override bool HasWorkToDo()
    {
        if (_workplace is not FarmingBuilding farm) return false;

        // Carried produce / inventory still pending deposit also counts as work-to-do —
        // otherwise a farmer who clocked off mid-cycle with goods in their bag would skip
        // their final deposit pass.
        bool hasAtLeastOneResource = false;
        if (_worker != null && _worker.CharacterEquipment != null && _worker.CharacterEquipment.HaveInventory())
        {
            hasAtLeastOneResource = _worker.CharacterEquipment.GetInventory().ItemSlots.Any(slot => !slot.IsEmpty());
        }
        if (!hasAtLeastOneResource && _worker?.CharacterVisual?.BodyPartsController?.HandsController != null)
        {
            if (_worker.CharacterVisual.BodyPartsController.HandsController.IsCarrying)
                hasAtLeastOneResource = true;
        }

        return hasAtLeastOneResource
            || HasAvailableTask<HarvestResourceTask>(farm)
            || HasAvailableTask<WaterCropTask>(farm)
            || HasAvailableTask<PlantCropTask>(farm);
    }

    /// <summary>
    /// Default farmer schedule: 6h–18h Work. Slightly later than Harvester's 6–16h so farmers can
    /// run evening watering passes after the harvesters have packed up.
    /// </summary>
    public override List<ScheduleEntry> GetWorkSchedule()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry(6, 18, ScheduleActivity.Work, 10)
        };
    }

    /// <summary>Override Assign so the farmer is registered as an employee of the FarmingBuilding.</summary>
    public override void Assign(Character worker, CommercialBuilding workplace)
    {
        base.Assign(worker, workplace);
        if (workplace is FarmingBuilding farm) farm.AddEmployee(worker);
    }

    /// <summary>Override Unassign so the farmer is removed from the FarmingBuilding's employee list.</summary>
    public override void Unassign()
    {
        if (_workplace is FarmingBuilding farm && _worker != null) farm.RemoveEmployee(_worker);

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
