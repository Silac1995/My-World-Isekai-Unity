using System.Collections.Generic;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// JobBuilder: NPC employed at an <see cref="AdministrativeBuilding"/> who consumes
/// <see cref="BuildOrder"/>s placed on the AB's <see cref="BuildingLogisticsManager"/>.
/// GOAP-driven, mirrors <see cref="JobFarmer"/>'s shape (cached goals, scratch worldState
/// dict, fresh action instances per plan, force-replan on action completion, 0.3 s tick).
///
/// Goal priority (highest first):
///   DeliverAndConstruct   — carrying a needed material + active order → drop in zone +
///                           run FinishBuildingConstruction (multi-trip via replan).
///   FetchFromABStorage    — active order + AB storage has the right material → walk +
///                           take from storage.
///   (Idle)                — active order but no material available, OR no order at all.
///                           No idle action library entry — the worker simply stands at
///                           the workplace until logistics fills the storage. JobFarmer's
///                           GoapAction_IdleInBuilding takes a HarvestingBuilding (which
///                           AdministrativeBuilding is not), so we drop it. The implicit
///                           idle is fine for v1.
///
/// Schedule: 6h-18h (same as JobFarmer / JobHarvester).
/// </summary>
[System.Serializable]
public class JobBuilder : Job
{
    [SerializeField] private string _jobTitle;
    [SerializeField] private JobType _jobType;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Builder;
    public override JobType Type => _jobType;

    // Heavy-planning job — match JobFarmer's 0.3 s cadence so multi-builder GOAP cost
    // stays predictable. See wiki/projects/optimisation-backlog.md entry #2.
    public override float ExecuteIntervalSeconds => 0.3f;

    // GOAP state
    private GoapGoal _currentGoal;
    private List<GoapAction> _availableActions;
    private List<GoapAction> _scratchValidActions = new List<GoapAction>(8);
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(16);
    private GoapGoal _cachedDeliverGoal;
    private GoapGoal _cachedFetchGoal;

    // 1 Hz throttle for the no-plan diagnostic dump (mirrors JobFarmer line 60).
    private float _lastIdleDumpTime = -10f;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _currentGoal != null ? _currentGoal.GoalName : "No Goal";

    /// <summary>Server-only convenience. Returns the first active BuildOrder on this
    /// builder's AB workplace, or null if none.</summary>
    public BuildOrder CurrentBuildOrder
    {
        get
        {
            if (_workplace == null) return null;
            var blm = _workplace.LogisticsManager;
            return blm != null ? blm.GetFirstActiveBuildOrder() : null;
        }
    }

    public JobBuilder(string jobTitle = "Builder", JobType jobType = JobType.Builder)
    {
        _jobTitle = jobTitle;
        _jobType = jobType;
    }

    public override void Execute()
    {
        if (_workplace == null || !(_workplace is AdministrativeBuilding ab)) return;

        // Tick the in-flight action (validity → execute → completion check). Mirrors
        // JobFarmer.Execute lines 76-102 verbatim.
        if (_currentAction != null)
        {
            if (!_currentAction.IsValid(_worker))
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=orange>[JobBuilder]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalid; replanning.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=cyan>[JobBuilder]</color> {_worker.CharacterName} : action {_currentAction.ActionName} complete.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                // Force replan on every action completion — same rationale as JobFarmer:
                // pop-the-plan would risk firing FinishConstruction with no material
                // already dropped, when in fact we should re-take from storage first.
                _currentPlan = null;
            }
            return;
        }

        // No action in flight → plan.
        PlanNextActions(ab);
    }

    /// <summary>
    /// Builds the worldState dict, picks the highest-priority achievable goal, runs
    /// <see cref="GoapPlanner.Plan"/>, and dequeues the first action.
    /// </summary>
    private void PlanNextActions(AdministrativeBuilding ab)
    {
        BuildOrder order = CurrentBuildOrder;
        bool hasActiveBuildOrder = order != null;

        // Carry-aware: do we hold ANY material the active order still needs?
        bool hasMaterialsInHand = false;
        if (hasActiveBuildOrder)
        {
            var carried = GetCarriedItem(_worker);
            if (carried != null)
            {
                foreach (var (item, missing) in order.GetMissingMaterials())
                {
                    if (item == carried.ItemSO) { hasMaterialsInHand = true; break; }
                }
            }
        }

        // Storage-aware: does AB's storage chain hold ANY missing material?
        bool hasMatchingMaterialInABStorage = false;
        if (hasActiveBuildOrder)
        {
            foreach (var (item, _) in order.GetMissingMaterials())
            {
                if (StorageContains(ab, item))
                {
                    hasMatchingMaterialInABStorage = true;
                    break;
                }
            }
        }

        // Inside-zone: are we already standing in the construction zone? Set this so a
        // builder who's already arrived doesn't backtrack to the AB.
        bool insideConstructionSite = false;
        if (hasActiveBuildOrder && order.TargetBuilding != null && order.TargetBuilding.BuildingZone != null)
        {
            var bounds = order.TargetBuilding.BuildingZone.bounds;
            var pos = _worker.transform.position;
            insideConstructionSite =
                pos.x >= bounds.min.x && pos.x <= bounds.max.x &&
                pos.z >= bounds.min.z && pos.z <= bounds.max.z;
        }

        _scratchWorldState.Clear();
        _scratchWorldState["hasActiveBuildOrder"] = hasActiveBuildOrder;
        _scratchWorldState["hasMaterialsInHand"] = hasMaterialsInHand;
        _scratchWorldState["hasMatchingMaterialInABStorage"] = hasMatchingMaterialInABStorage;
        _scratchWorldState["insideConstructionSite"] = insideConstructionSite;
        _scratchWorldState["materialDelivered"] = false;
        _scratchWorldState["isIdling"] = false;

        if (_availableActions == null) _availableActions = new List<GoapAction>(4);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_TakeMaterialFromABStorage(ab));
        _availableActions.Add(new GoapAction_GoToConstructionSite(ab));
        _availableActions.Add(new GoapAction_DropMaterialAtZone(ab));
        _availableActions.Add(new GoapAction_FinishBuildingConstruction(ab));

        if (_cachedDeliverGoal == null)
            _cachedDeliverGoal = new GoapGoal("DeliverAndConstruct",
                new Dictionary<string, bool> { { "isIdling", true } }, priority: 5);
        if (_cachedFetchGoal == null)
            _cachedFetchGoal = new GoapGoal("FetchFromABStorage",
                new Dictionary<string, bool> { { "hasMaterialsInHand", true } }, priority: 4);

        // ── Pick highest-priority goal whose plan is achievable ──

        GoapGoal targetGoal;
        if (hasMaterialsInHand && hasActiveBuildOrder)              targetGoal = _cachedDeliverGoal;
        else if (hasActiveBuildOrder && hasMatchingMaterialInABStorage) targetGoal = _cachedFetchGoal;
        else                                                         targetGoal = null;

        if (targetGoal == null)
        {
            // No achievable goal — idle. Worker stands at workplace. Diagnostic dump
            // throttled to 1 Hz.
            _currentGoal = null;
            _currentAction = null;
            _currentPlan = null;

            if (_workplace != null && _workplace.IsWorkerOnShift(_worker))
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now - _lastIdleDumpTime > 1f)
                {
                    _lastIdleDumpTime = now;
                    if (NPCDebug.VerboseJobs)
                    {
                        Debug.Log(
                            $"<color=orange>[JobBuilder]</color> {_worker.CharacterName} idle at {ab.BuildingName}. " +
                            $"hasActiveBuildOrder={hasActiveBuildOrder}, hasMaterialsInHand={hasMaterialsInHand}, " +
                            $"hasMatchingMaterialInABStorage={hasMatchingMaterialInABStorage}, " +
                            $"insideConstructionSite={insideConstructionSite}.");
                    }
                }
            }
            return;
        }

        _currentGoal = targetGoal;

        // Pre-filter actions by IsValid (planner does NOT call IsValid). Same rationale
        // as JobFarmer._scratchValidActions: without this, identical-prec/effect actions
        // with different costs cause infinite-replan loops.
        _scratchValidActions.Clear();
        for (int i = 0; i < _availableActions.Count; i++)
        {
            var a = _availableActions[i];
            if (a.IsValid(_worker)) _scratchValidActions.Add(a);
        }

        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[JobBuilder]</color> {_worker.CharacterName} : new plan ({_currentGoal.GoalName}); first action → {_currentAction.ActionName}");
        }
        else if (_workplace != null && _workplace.IsWorkerOnShift(_worker))
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastIdleDumpTime > 1f)
            {
                _lastIdleDumpTime = now;

                var validActionNames = new System.Text.StringBuilder();
                for (int i = 0; i < _scratchValidActions.Count; i++)
                {
                    if (i > 0) validActionNames.Append(",");
                    validActionNames.Append(_scratchValidActions[i].ActionName);
                }

                Debug.Log(
                    $"<color=red>[JobBuilder]</color> {_worker.CharacterName} NO-PLAN for goal '{targetGoal.GoalName}' at {ab.BuildingName}. " +
                    $"validActions=[{validActionNames}]. worldState: " +
                    $"hasActiveBuildOrder={hasActiveBuildOrder}, hasMaterialsInHand={hasMaterialsInHand}, " +
                    $"hasMatchingMaterialInABStorage={hasMatchingMaterialInABStorage}, " +
                    $"insideConstructionSite={insideConstructionSite}.");
            }
        }
    }

    /// <summary>
    /// Walks worker bag + hands and returns the first carried item. Mirrors
    /// <see cref="GoapAction_GatherStorageItems"/>'s helper.
    /// </summary>
    private static ItemInstance GetCarriedItem(Character worker)
    {
        var inventory = worker.CharacterEquipment?.GetInventory();
        if (inventory != null && inventory.ItemSlots.Exists(s => !s.IsEmpty()))
        {
            return inventory.ItemSlots.FindLast(s => !s.IsEmpty()).ItemInstance;
        }
        return worker.CharacterVisual?.BodyPartsController?.HandsController?.CarriedItem;
    }

    /// <summary>
    /// Walks every <see cref="StorageFurniture"/> in the AB's transform tree and returns
    /// true if any slot holds <paramref name="target"/>. Mirrors
    /// <see cref="GoapAction_TakeMaterialFromABStorage"/>'s storage walk verbatim — the
    /// fetch side is "find it wherever it physically is."
    /// </summary>
    private static bool StorageContains(CommercialBuilding building, ItemSO target)
    {
        if (building == null || target == null) return false;
        var storages = building.GetComponentsInChildren<StorageFurniture>();
        for (int i = 0; i < storages.Length; i++)
        {
            var sf = storages[i];
            if (sf == null) continue;
            var slots = sf.ItemSlots;
            if (slots == null) continue;
            for (int s = 0; s < slots.Count; s++)
            {
                var slot = slots[s];
                if (slot == null || slot.IsEmpty()) continue;
                if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == target)
                    return true;
            }
        }
        return false;
    }

    public override bool CanExecute() => base.CanExecute() && _workplace is AdministrativeBuilding;

    public override bool HasWorkToDo()
    {
        if (_workplace is not AdministrativeBuilding ab) return false;
        if (ab.LogisticsManager != null && ab.LogisticsManager.ActiveBuildOrders.Count > 0) return true;

        // Carry-completion guard: if we're carrying anything, deposit-trip is work-to-do.
        // Mirrors JobFarmer.HasWorkToDo's carried-item branch so a builder who clocks off
        // mid-trip still completes their final deposit.
        if (_worker == null) return false;
        var hands = _worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.IsCarrying) return true;
        var inv = _worker.CharacterEquipment != null && _worker.CharacterEquipment.HaveInventory()
            ? _worker.CharacterEquipment.GetInventory()
            : null;
        if (inv != null && inv.ItemSlots != null)
        {
            for (int i = 0; i < inv.ItemSlots.Count; i++)
            {
                if (inv.ItemSlots[i] != null && !inv.ItemSlots[i].IsEmpty()) return true;
            }
        }
        return false;
    }

    public override List<ScheduleEntry> GetWorkSchedule()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry(6, 18, ScheduleActivity.Work, 10)
        };
    }

    public override void Unassign()
    {
        // GOAP cleanup — mirrors JobFarmer.Unassign. AdministrativeBuilding does not
        // expose AddEmployee/RemoveEmployee (those are HarvestingBuilding-specific for
        // the harvest-site worker list); Job._worker is the source of truth for "who's
        // working" on a CommercialBuilding job and that's already wired in base.Unassign.
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;

        base.Unassign();
    }
}
