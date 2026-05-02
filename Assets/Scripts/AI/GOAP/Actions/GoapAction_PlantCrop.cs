using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;
using MWI.Terrain;
using MWI.WorldSystem;

/// <summary>
/// Wraps <see cref="MWI.Farming.CharacterAction_PlaceCrop"/>. Walks worker to the cell of
/// a claimed <c>PlantCropTask</c>, queues the CharacterAction, marks complete. The
/// CharacterAction itself consumes the held seed, sets <c>cell.PlantedCropId</c>, and
/// spawns the <c>CropHarvestable</c> via <c>FarmGrowthSystem.SpawnHarvestableAt</c>
/// (existing 2026-04-29 farming substrate handles the rest).
///
/// Cost = 1.
/// Preconditions:
///   hasSeedInHand = true
///   hasUnfilledPlantTask = true
/// Effects:
///   hasPlantedCrop = true
///   hasSeedInHand = false
///
/// Preconditions match <see cref="GoapAction_FetchSeed"/>'s effects so the planner can
/// chain FetchSeed → PlantCrop in a single plan.
///
/// Race-loss handling: claims the PlantCropTask via
/// <see cref="BuildingTaskManager.ClaimBestTask{T}"/> with a predicate that filters
/// to tasks whose crop matches the held seed. If no such task can be claimed (another
/// farmer beat us, or the task expired), Execute marks complete with no effect; the
/// planner re-plans (typically falling through to a return-seed-to-storage action or a
/// no-op idle until the next OnNewDay scan repopulates tasks).
/// </summary>
public class GoapAction_PlantCrop : GoapAction
{
    private readonly FarmingBuilding _building;

    private bool _isMoving;
    private bool _isComplete;
    private PlantCropTask _claimedTask;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "PlantCrop";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_PlantCrop(FarmingBuilding building)
    {
        _building = building;

        _preconditions = new Dictionary<string, bool>
        {
            { "hasSeedInHand", true },
            { "hasUnfilledPlantTask", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { "hasPlantedCrop", true },
            { "hasSeedInHand", false }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null) return false;
        if (_building.TaskManager == null) return false;

        // Do NOT require seed-in-hand here. That state is a *precondition* used by the
        // planner to chain FetchSeed → PlantCrop. JobFarmer pre-filters _availableActions
        // by IsValid before calling GoapPlanner.Plan; if PlantCrop fails IsValid because
        // the worker isn't yet carrying a seed (which is true at the START of every plan
        // tick before FetchSeed has run), the planner never sees PlantCrop as a candidate
        // and the FetchSeed→PlantCrop chain cannot be built. Result: no plan for the
        // PlantEmptyCells goal → JobFarmer falls through to Idle even when seeds + tasks
        // both exist. Same anti-pattern caught at GoapAction_FetchToolFromStorage which
        // (correctly) only requires hands-free, not tool-in-hand.
        //
        // Just check that at least one actionable PlantCropTask exists (any crop).
        // Crop-matching against the held seed is the *Execute*-time concern, not the
        // plan-time validity gate — the runtime ClaimBestTask predicate already filters
        // by held seed there.

        if (worker.CharacterVisual?.BodyPartsController?.HandsController == null) return false;

        // Filter by task.IsValid() so cross-actor races (player plants the cell first, etc.)
        // get caught: an obsolete PlantCropTask returns IsValid=false from its cell-state
        // check, ClaimBestTask drops it from _availableTasks, and we don't pretend the work
        // is still pending here.
        var available = _building.TaskManager.AvailableTasks;
        for (int i = 0; i < available.Count; i++)
        {
            if (available[i] is PlantCropTask pctA && pctA.IsValid()) return true;
        }
        var inProgress = _building.TaskManager.InProgressTasks;
        for (int i = 0; i < inProgress.Count; i++)
        {
            if (inProgress[i] is PlantCropTask pct && pct.ClaimedByWorkers.Contains(worker) && pct.IsValid()) return true;
        }
        return false;
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
            var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands == null || hands.CarriedItem == null || !(hands.CarriedItem.ItemSO is SeedSO heldSeed))
            {
                _isComplete = true;
                return;
            }

            // First check for a pre-claimed task (quest-system auto-claim path); fall
            // back to ClaimBestTask which walks _availableTasks only.
            _claimedTask = _building.TaskManager.FindClaimedTaskByWorker<PlantCropTask>(worker,
                t => t.Crop == heldSeed.CropToPlant);
            if (_claimedTask == null)
            {
                _claimedTask = _building.TaskManager.ClaimBestTask<PlantCropTask>(worker,
                    t => t.Crop == heldSeed.CropToPlant);
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

        // Final cross-actor race check before queueing the action. Between the time we
        // claimed the task and now (worker walked to the cell), the player or another NPC
        // may have planted this cell — re-checking task.IsValid() catches that and we
        // abort cleanly without overwriting / no-oping. Mirror in GoapAction_WaterCrop.
        if (!_claimedTask.IsValid())
        {
            // Release the claim so a (now hypothetical, since the cell is taken) future
            // task on this cell isn't blocked by a stale claim, and let the planner pick
            // another cell on the next replan.
            _building.TaskManager.UnclaimTask(_claimedTask, worker);
            _isComplete = true;
            return;
        }

        // At the cell. Queue CharacterAction_PlaceCrop. The action's OnApplyEffect
        // consumes the held seed, mutates the cell, and spawns the CropHarvestable.
        var crop = _claimedTask.Crop;
        worker.CharacterActions?.ExecuteAction(
            new MWI.Farming.CharacterAction_PlaceCrop(worker, map, _claimedTask.CellX, _claimedTask.CellZ, crop));

        // Mark the BuildingTask complete (removes from both buckets, fires OnTaskCompleted
        // for the IQuest publish path).
        _building.TaskManager.CompleteTask(_claimedTask);

        if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=green>[PlantCrop]</color> {worker.CharacterName} planted {crop.Id} at ({_claimedTask.CellX}, {_claimedTask.CellZ}).");
        }

        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        // Race-loss / interruption: if we claimed a task but never queued the
        // CharacterAction, return the claim to the pool so another worker can take it.
        if (_claimedTask != null && !_isComplete && _building != null && _building.TaskManager != null)
        {
            _building.TaskManager.UnclaimTask(_claimedTask, worker);
        }

        _isMoving = false;
        _isComplete = false;
        _claimedTask = null;
    }
}
