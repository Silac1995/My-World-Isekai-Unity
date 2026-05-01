using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;
using MWI.Terrain;
using MWI.WorldSystem;

/// <summary>
/// Wraps <see cref="MWI.Farming.CharacterAction_WaterCrop"/>. Walks worker (with the
/// building's WateringCan in hand — fetched via
/// <see cref="GoapAction_FetchToolFromStorage"/>(<c>WateringCanItem</c>) earlier in the
/// plan) to the cell of a claimed <c>WaterCropTask</c>, queues the CharacterAction, marks
/// complete.
///
/// Cost = 1.
/// Preconditions:
///   hasToolInHand_&lt;wateringCanItem.name&gt; = true   (Plan 1 ToolStorage primitive convention)
///   hasUnfilledWaterTask = true
/// Effects:
///   hasWateredCell = true
///
/// Reuses Plan 1's tool-storage key shape (<c>hasToolInHand_{itemName}=true</c>) so the
/// planner can chain
/// GoapAction_FetchToolFromStorage(WateringCan) → GoapAction_WaterCrop →
/// GoapAction_ReturnToolToStorage(WateringCan) end-to-end without bespoke glue state.
///
/// Race-loss handling: claims the WaterCropTask via
/// <see cref="BuildingTaskManager.ClaimBestTask{T}"/>. If no task is available
/// (another worker beat us, or the cell got rained on between OnNewDay and now), Execute
/// marks complete with no effect; planner re-plans.
/// </summary>
public class GoapAction_WaterCrop : GoapAction
{
    private readonly FarmingBuilding _building;

    private bool _isMoving;
    private bool _isComplete;
    private WaterCropTask _claimedTask;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "WaterCrop";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_WaterCrop(FarmingBuilding building)
    {
        _building = building;

        // Tool key uses the WateringCanItem's asset name (Plan 1 convention — see
        // GoapAction_FetchToolFromStorage.ToolKey()). When the building has no can wired,
        // fall through with "null" so the action declares an unsatisfiable precondition
        // rather than crashing — IsValid will reject anyway.
        string canKey = _building != null && _building.WateringCanItem != null
            ? _building.WateringCanItem.name
            : "null";

        _preconditions = new Dictionary<string, bool>
        {
            { $"hasToolInHand_{canKey}", true },
            { "hasUnfilledWaterTask", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { "hasWateredCell", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null) return false;
        if (_building.WateringCanItem == null) return false;
        if (_building.TaskManager == null) return false;

        // Do NOT require can-in-hand here. That state is a *precondition*
        // (hasToolInHand_{canKey}=true) the planner uses to chain
        // FetchToolFromStorage(WateringCan) → WaterCrop. Pre-filtering by hand-state
        // would knock WaterCrop out of _scratchValidActions at plan time (when hands
        // are empty), so the planner could never build the chain. Same anti-pattern
        // fixed in GoapAction_PlantCrop.

        if (worker.CharacterVisual?.BodyPartsController?.HandsController == null) return false;

        var available = _building.TaskManager.AvailableTasks;
        for (int i = 0; i < available.Count; i++)
        {
            if (available[i] is WaterCropTask) return true;
        }
        // Fall through: a pre-claimed WaterCropTask sitting on this worker (quest-system
        // auto-claim path) — same pattern as GoapAction_PlantCrop.
        return _building.TaskManager.FindClaimedTaskByWorker<WaterCropTask>(worker) != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (worker == null || _building == null || _building.TaskManager == null)
        {
            _isComplete = true;
            return;
        }

        if (_claimedTask == null)
        {
            // Pre-claimed (quest-system auto-claim) path first; then fall back to
            // ClaimBestTask which walks _availableTasks only.
            _claimedTask = _building.TaskManager.FindClaimedTaskByWorker<WaterCropTask>(worker);
            if (_claimedTask == null)
            {
                _claimedTask = _building.TaskManager.ClaimBestTask<WaterCropTask>(worker);
            }

            if (_claimedTask == null)
            {
                _isComplete = true;
                return;
            }
        }

        var map = MapController.GetMapAtPosition(_building.transform.position);
        if (map == null) { _isComplete = true; return; }

        var grid = map.GetComponent<TerrainCellGrid>();
        if (grid == null) { _isComplete = true; return; }

        Vector3 cellWorld = grid.GridToWorld(_claimedTask.CellX, _claimedTask.CellZ);
        if (Vector3.Distance(worker.transform.position, cellWorld) > 1.5f)
        {
            if (!_isMoving)
            {
                worker.CharacterMovement?.SetDestination(cellWorld);
                _isMoving = true;
            }
            return;
        }

        // Resolve moisture-set value from the held WateringCan instance (designer-tunable
        // per-can on WateringCanSO.MoistureSetTo). Fall back to 1f (full saturation) if
        // the held item isn't a WateringCanSO subtype, which shouldn't happen given
        // IsValid's gate but matches the tolerant pattern used in the player path.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        var canSO = hands?.CarriedItem?.ItemSO as WateringCanSO;
        float moisture = canSO != null ? canSO.MoistureSetTo : 1f;

        worker.CharacterActions?.ExecuteAction(
            new MWI.Farming.CharacterAction_WaterCrop(worker, map, _claimedTask.CellX, _claimedTask.CellZ, moisture));

        _building.TaskManager.CompleteTask(_claimedTask);

        if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=cyan>[WaterCrop]</color> {worker.CharacterName} watered ({_claimedTask.CellX}, {_claimedTask.CellZ}) with moisture={moisture}.");
        }

        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        // Race-loss / interruption: return the claim so another worker can take it.
        if (_claimedTask != null && !_isComplete && _building != null && _building.TaskManager != null)
        {
            _building.TaskManager.UnclaimTask(_claimedTask, worker);
        }

        _isMoving = false;
        _isComplete = false;
        _claimedTask = null;
    }
}
