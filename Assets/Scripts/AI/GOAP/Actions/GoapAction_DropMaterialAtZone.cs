using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JobBuilder delivery action. Once the worker is inside the construction
/// <see cref="Building.BuildingZone"/> and carrying a material, queues a
/// <see cref="CharacterDropItem"/> so the carried instance becomes a loose
/// <see cref="WorldItem"/> in the zone, where
/// <see cref="CharacterAction_FinishConstruction.ConsumeFromZone"/> can despawn it next
/// tick.
///
/// Cost = 0.5 (cheap — single drop animation).
///
/// Preconditions:
///   hasMaterialsInHand = true
///   insideConstructionSite = true
/// Effects:
///   materialDelivered = true
///
/// Wait pattern (mirrors <see cref="GoapAction_GatherStorageItems"/>'s DroppingOff
/// branch): queue the action via <c>CharacterActions.ExecuteAction</c>, subscribe to
/// <c>OnActionFinished</c>, set _isComplete inside the callback so the GOAP planner
/// doesn't move on until the 0.5 s drop animation actually fires.
/// </summary>
public class GoapAction_DropMaterialAtZone : GoapAction
{
    private readonly AdministrativeBuilding _ab;

    private bool _isComplete;
    private bool _actionStarted;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "DropMaterialAtZone";
    public override float Cost => 0.5f;
    public override bool IsComplete => _isComplete;

    public GoapAction_DropMaterialAtZone(AdministrativeBuilding ab)
    {
        _ab = ab;

        _preconditions = new Dictionary<string, bool>
        {
            { "hasMaterialsInHand", true },
            { "insideConstructionSite", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { "materialDelivered", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _ab == null) return false;

        // Protective hold: once the drop animation is in flight, keep the action valid so
        // the OnActionFinished callback gets a chance to fire.
        if (_actionStarted) return true;

        var order = GetActiveBuildOrder();
        if (order == null || order.TargetBuilding == null) return false;
        if (!order.TargetBuilding.IsUnderConstruction) return false;

        // Worker must still be in the construction zone AND carrying something.
        if (order.TargetBuilding.BuildingZone == null) return false;
        var bounds = order.TargetBuilding.BuildingZone.bounds;
        var pos = worker.transform.position;
        bool inside = pos.x >= bounds.min.x && pos.x <= bounds.max.x
                   && pos.z >= bounds.min.z && pos.z <= bounds.max.z;
        if (!inside) return false;

        return GetCarriedItem(worker) != null;
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

        ItemInstance carried = GetCarriedItem(worker);
        if (carried == null)
        {
            _isComplete = true;
            return;
        }

        var dropAction = new CharacterDropItem(worker, carried);
        if (worker.CharacterActions.ExecuteAction(dropAction))
        {
            _actionStarted = true;
            dropAction.OnActionFinished += () =>
            {
                _isComplete = true;
                if (NPCDebug.VerboseJobs)
                {
                    Debug.Log($"<color=cyan>[DropMaterialAtZone]</color> {worker.CharacterName} dropped {carried.ItemSO.ItemName} at construction site.");
                }
            };
        }
        else
        {
            // Action refused (probably another action in flight). Purge + force-finish so
            // the planner can re-evaluate. Mirrors GatherStorageItems' refusal path.
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

    /// <summary>
    /// Mirrors <see cref="GoapAction_GatherStorageItems.GetCarriedItem"/> — checks the
    /// worker's bag first (LIFO), then falls back to the hands controller's carried item.
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

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _actionStarted = false;
    }
}
