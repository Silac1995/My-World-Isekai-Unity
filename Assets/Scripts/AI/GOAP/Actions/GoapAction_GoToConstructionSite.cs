using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JobBuilder move action. Walks the worker into the active <see cref="BuildOrder"/>'s
/// <see cref="Building.BuildingZone"/> (the construction footprint).
///
/// The construction zone is a plain BoxCollider trigger (Building.BuildingZone) — it does
/// NOT carry an <see cref="InteractableObject"/> in v1, so arrival is gated on 2D X-Z
/// bounds containment (same shape as
/// <see cref="CharacterAction_FinishConstruction.IsActorInsideBuildingZone"/>). The 2D
/// check mirrors the Y-noise-robust pattern from that action.
///
/// Cost = 0.5 (cheap — pure move; the planner prefers this over alternative routes).
///
/// Preconditions:
///   hasMaterialsInHand = true     (we only walk after picking up a material)
///   hasActiveBuildOrder = true
/// Effects:
///   insideConstructionSite = true
/// </summary>
public class GoapAction_GoToConstructionSite : GoapAction
{
    private readonly AdministrativeBuilding _ab;

    private bool _isMoving;
    private bool _isComplete;
    private Vector3 _targetPos;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "GoToConstructionSite";
    public override float Cost => 0.5f;
    public override bool IsComplete => _isComplete;

    public GoapAction_GoToConstructionSite(AdministrativeBuilding ab)
    {
        _ab = ab;

        _preconditions = new Dictionary<string, bool>
        {
            { "hasMaterialsInHand", true },
            { "hasActiveBuildOrder", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { "insideConstructionSite", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _ab == null) return false;
        var order = GetActiveBuildOrder();
        if (order == null || order.TargetBuilding == null) return false;
        if (!order.TargetBuilding.IsUnderConstruction) return false;
        if (order.TargetBuilding.BuildingZone == null) return false;
        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (worker == null || _ab == null)
        {
            _isComplete = true;
            return;
        }

        var order = GetActiveBuildOrder();
        if (order == null || order.TargetBuilding == null || order.TargetBuilding.BuildingZone == null)
        {
            _isComplete = true;
            return;
        }

        var bounds = order.TargetBuilding.BuildingZone.bounds;

        // 2D X-Z containment — mirrors CharacterAction_FinishConstruction.IsActorInsideBuildingZone.
        Vector3 pos = worker.transform.position;
        bool inside = pos.x >= bounds.min.x && pos.x <= bounds.max.x
                   && pos.z >= bounds.min.z && pos.z <= bounds.max.z;

        if (inside)
        {
            worker.CharacterMovement?.ResetPath();
            _isComplete = true;
            return;
        }

        _targetPos = bounds.center;

        // Softlock guard — path landed just outside the zone (rule #36 pattern).
        var movement = worker.CharacterMovement;
        bool pathExhausted = movement != null
            && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f);
        if (pathExhausted)
        {
            Vector3 wp = pos;
            Vector3 a = new Vector3(wp.x, 0f, wp.z);
            Vector3 b = new Vector3(_targetPos.x, 0f, _targetPos.z);
            if (Vector3.Distance(a, b) <= 2f)
            {
                // Close enough — accept arrival even though bounds-Contains failed.
                movement?.ResetPath();
                _isComplete = true;
                return;
            }
        }

        // Re-fire SetDestination on path loss — rule #36 anti-freeze pattern.
        if (!_isMoving || (movement != null && !movement.HasPath))
        {
            movement?.SetDestination(_targetPos);
            _isMoving = true;
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
        _isMoving = false;
        _isComplete = false;
    }
}
