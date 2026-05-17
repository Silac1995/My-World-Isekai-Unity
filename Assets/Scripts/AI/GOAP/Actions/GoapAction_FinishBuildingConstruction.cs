using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JobBuilder construction-progress action. Once the worker is inside the construction
/// <see cref="Building.BuildingZone"/> AND has delivered at least one material this trip,
/// queues a <see cref="CharacterAction_FinishConstruction"/> (the Phase 1 continuous
/// action). That continuous action's per-tick <c>ConsumeFromZone</c> despawns matching
/// <see cref="WorldItem"/>s inside the zone and bumps <see cref="Building.ConstructionProgress"/>
/// until it reaches 1.0, at which point <see cref="Building.Finalize"/> flips the state
/// and the action returns "done".
///
/// Cost = 1.0 (parity with FetchSeed / standard work actions).
///
/// Preconditions:
///   insideConstructionSite = true
///   materialDelivered = true
/// Effects:
///   isIdling = true     (parent goal "DeliverAndConstruct" wraps this — the planner
///                        will replan after completion; multi-trip is handled by
///                        JobBuilder's force-replan on action completion)
///
/// Wait pattern: queue the continuous action, subscribe to <c>OnActionFinished</c>, set
/// <c>_isComplete</c> inside the callback. The continuous action ends when (a) progress
/// hits 1.0 → Finalize, (b) the worker leaves the zone, (c) MaxStallTicks of zero-consume.
/// All three drop us back into JobBuilder.PlanNextActions to pick the next trip.
/// </summary>
public class GoapAction_FinishBuildingConstruction : GoapAction
{
    private readonly AdministrativeBuilding _ab;

    private bool _isComplete;
    private bool _actionStarted;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "FinishBuildingConstruction";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_FinishBuildingConstruction(AdministrativeBuilding ab)
    {
        _ab = ab;

        _preconditions = new Dictionary<string, bool>
        {
            { "insideConstructionSite", true },
            { "materialDelivered", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { "isIdling", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _ab == null) return false;

        // Protective hold while the continuous action ticks server-side.
        if (_actionStarted) return true;

        var order = GetActiveBuildOrder();
        if (order == null || order.TargetBuilding == null) return false;
        if (!order.TargetBuilding.IsUnderConstruction) return false;
        if (order.TargetBuilding.BuildingZone == null) return false;

        // Worker must still be inside the zone.
        var bounds = order.TargetBuilding.BuildingZone.bounds;
        var pos = worker.transform.position;
        bool inside = pos.x >= bounds.min.x && pos.x <= bounds.max.x
                   && pos.z >= bounds.min.z && pos.z <= bounds.max.z;
        return inside;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (worker == null)
        {
            _isComplete = true;
            return;
        }

        if (_actionStarted) return; // waiting for OnActionFinished

        var order = GetActiveBuildOrder();
        if (order == null || order.TargetBuilding == null)
        {
            _isComplete = true;
            return;
        }

        var charAction = new CharacterAction_FinishConstruction(worker, order.TargetBuilding);
        if (worker.CharacterActions.ExecuteAction(charAction))
        {
            _actionStarted = true;
            charAction.OnActionFinished += () =>
            {
                _isComplete = true;
                if (NPCDebug.VerboseJobs)
                {
                    Debug.Log($"<color=cyan>[FinishBuildingConstruction]</color> {worker.CharacterName} finished construction tick on '{order.TargetBuilding.BuildingName}' (progress={order.TargetBuilding.ConstructionProgress.Value:P0}).");
                }
            };
        }
        else
        {
            if (worker.CharacterActions.CurrentAction != null)
            {
                worker.CharacterActions.ClearCurrentAction();
            }
            _isComplete = true;
        }
    }

    private BuildOrder GetActiveBuildOrder()
    {
        if (_ab == null) return null;
        var blm = _ab.LogisticsManager;
        return blm != null ? blm.GetFirstActiveBuildOrder() : null;
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _actionStarted = false;
    }
}
