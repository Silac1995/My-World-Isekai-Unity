using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic GOAP action: walk to the building's _toolStorageFurniture, drop the tool
/// currently held in the worker's hands into the storage, and clear the building-
/// ownership stamp. Symmetric mirror of <see cref="GoapAction_FetchToolFromStorage"/>
/// (Task 3 of the tool-storage plan); composable in any worker plan that completes a
/// task involving a building-owned tool.
///
/// Cost = 1 (parity with the fetch action).
///
/// Preconditions:
///   - hasToolInHand_{itemSO.name} == true
///   - taskCompleteForTool_{itemSO.name} == true
/// Effects:
///   - hasToolInHand_{itemSO.name} == false
///   - toolReturned_{itemSO.name} == true
///
/// IsValid:
///   - building.ToolStorage != null
///   - storage is NOT full (an empty compatible slot exists for the tool)
///   - worker carries (in their HANDS) an ItemInstance whose ItemSO == toolItem AND
///     whose OwnerBuildingId == building.BuildingId. The owner-id check enforces the
///     symmetric contract: this action only returns tools that came from THIS building.
///
/// Notes:
///   - Movement gating uses <see cref="InteractableObject.IsCharacterInInteractionZone(Character)"/>
///     when available (per project convention), with a fallback to a flat-XZ proximity test for
///     storage furniture that doesn't expose an InteractionZone collider — exactly mirroring the
///     fetch action.
///   - The OwnerBuildingId clear happens automatically inside <see cref="StorageFurniture.AddItem"/>
///     (Part A of Task 4): when the destination storage matches the origin building, AddItem
///     resets the stamp before the item lands in a slot. This single hook also covers the
///     player drop-in path (no GOAP), so BOTH return routes converge on the same clearing logic.
///   - Storage-full fallback: if AddItem returns false (storage was just filled by another worker
///     or the slot got locked), the worker re-equips the tool to their hands so they aren't
///     gated forever, and the OwnerBuildingId is cleared manually since the tool didn't actually
///     return to storage. The planner replans on next tick.
/// </summary>
public class GoapAction_ReturnToolToStorage : GoapAction
{
    private readonly CommercialBuilding _building;
    private readonly ItemSO _toolItem;
    private readonly InteractableObject _storageInteractable;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    private bool _isMoving;
    private bool _isComplete;

    public override string ActionName => $"ReturnTool({(_toolItem != null ? _toolItem.ItemName : "?")})";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;

    public GoapAction_ReturnToolToStorage(CommercialBuilding building, ItemSO toolItem)
    {
        _building = building;
        _toolItem = toolItem;
        // Cache the storage's InteractableObject once. _toolStorageFurniture on
        // CommercialBuilding is a serialized prefab field — set at design time, never
        // swapped at runtime — so the lookup is safe to memoize for the action's lifetime.
        _storageInteractable = building?.ToolStorage != null
            ? building.ToolStorage.GetComponent<InteractableObject>()
            : null;

        string key = ToolKey();
        _preconditions = new Dictionary<string, bool>
        {
            { $"hasToolInHand_{key}", true },
            { $"taskCompleteForTool_{key}", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { $"hasToolInHand_{key}", false },
            { $"toolReturned_{key}", true }
        };
    }

    private string ToolKey() => _toolItem != null ? _toolItem.name : "null";

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null || _toolItem == null) return false;
        if (_building.ToolStorage == null) return false;

        // Storage must have at least one empty compatible slot — otherwise the AddItem
        // call inside Execute would silently bounce. Gating here lets the planner pick a
        // different return route (e.g. drop-on-ground fallback if/when one is added).
        if (_building.ToolStorage.IsFull) return false;

        // Worker must hold the matching tool stamped with THIS building's id.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying) return false;
        var carried = hands.CarriedItem;
        if (carried == null) return false;
        if (carried.ItemSO != _toolItem) return false;
        if (carried.OwnerBuildingId != _building.BuildingId) return false;

        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (worker == null || _building == null || _building.ToolStorage == null || _toolItem == null)
        {
            _isComplete = true;
            return;
        }

        var storage = _building.ToolStorage;

        // Movement gate: prefer InteractableObject.IsCharacterInInteractionZone
        // (canonical proximity API). Fall back to a flat-XZ distance check when the
        // storage doesn't expose an InteractionZone collider. The InteractableObject
        // reference is cached once in the constructor (see _storageInteractable).
        var interactable = _storageInteractable;
        bool inZone;
        if (interactable != null && interactable.InteractionZone != null)
        {
            inZone = interactable.IsCharacterInInteractionZone(worker);
        }
        else
        {
            Vector3 ip = storage.GetInteractionPosition(worker.transform.position);
            Vector3 wp = worker.transform.position;
            Vector3 a = new Vector3(wp.x, 0f, wp.z);
            Vector3 b = new Vector3(ip.x, 0f, ip.z);
            inZone = Vector3.Distance(a, b) <= 1.5f;
        }

        if (!inZone)
        {
            if (!_isMoving)
            {
                Vector3 target = storage.GetInteractionPosition(worker.transform.position);
                worker.CharacterMovement?.SetDestination(target);
                _isMoving = true;
            }
            return;
        }

        // In zone — perform the return.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null)
        {
            _isComplete = true;
            return;
        }

        ItemInstance instance = hands.DropCarriedItem();
        if (instance == null)
        {
            // Race lost / hand cleared by another system between IsValid and Execute.
            _isComplete = true;
            return;
        }

        // Place into storage. The AddItem hook (Part A of Task 4) auto-clears
        // OwnerBuildingId when the destination matches the origin building.
        bool added = storage.AddItem(instance);
        if (!added)
        {
            // Storage filled (or got locked) between IsValid and now. Put the tool
            // back into the worker's inventory so they aren't permanently gated, and
            // clear the OwnerBuildingId manually since the tool did NOT return to
            // storage and the AddItem hook never ran.
            instance.OwnerBuildingId = "";
            worker.CharacterEquipment?.PickUpItem(instance);

            if (NPCDebug.VerboseJobs)
            {
                Debug.Log($"<color=orange>[ReturnTool]</color> {worker.CharacterName} could not return {_toolItem.ItemName} to {_building.name} (storage full/locked). Tool returned to inventory; OwnerBuildingId cleared.");
            }

            _isComplete = true;
            return;
        }

        if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=cyan>[ReturnTool]</color> {worker.CharacterName} returned {_toolItem.ItemName} to {_building.name} tool storage. OwnerBuildingId cleared by AddItem hook.");
        }

        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        _isComplete = false;
    }
}
