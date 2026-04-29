using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic GOAP action: walk to the building's _toolStorageFurniture, take 1 ItemInstance
/// matching <paramref name="toolItem"/>, stamp it with the building's BuildingId, and equip
/// it in the worker's HandsController. Composable in any worker plan that needs a building-
/// owned tool. Companion to <c>GoapAction_ReturnToolToStorage</c> (Task 4 of the tool-storage
/// plan).
///
/// Cost = 1 (parity with FetchSeed and other low-cost prelude actions).
///
/// Preconditions:
///   - hasToolInHand_{itemSO.name} == false
///   - toolNeededForTask_{itemSO.name} == true
/// Effects:
///   - hasToolInHand_{itemSO.name} == true
///
/// IsValid:
///   - building.ToolStorage != null
///   - storage contains at least 1 instance whose ItemSO == toolItem
///
/// Notes:
///   - Movement gating uses <see cref="InteractableObject.IsCharacterInInteractionZone(Character)"/>
///     when available (per project convention), with a fallback to a flat-XZ proximity test for
///     storage furniture that doesn't expose an InteractionZone collider.
///   - <see cref="ItemInstance.OwnerBuildingId"/> is stamped BEFORE the equip attempt so the
///     ownership marker is set even if the equip call fails for some reason (race with another
///     worker, hand already occupied with an unrecoverable item, etc.).
/// </summary>
public class GoapAction_FetchToolFromStorage : GoapAction
{
    private readonly CommercialBuilding _building;
    private readonly ItemSO _toolItem;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    private bool _isMoving;
    private bool _isComplete;

    public override string ActionName => $"FetchTool({(_toolItem != null ? _toolItem.ItemName : "?")})";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;

    public GoapAction_FetchToolFromStorage(CommercialBuilding building, ItemSO toolItem)
    {
        _building = building;
        _toolItem = toolItem;

        string key = ToolKey();
        _preconditions = new Dictionary<string, bool>
        {
            { $"hasToolInHand_{key}", false },
            { $"toolNeededForTask_{key}", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { $"hasToolInHand_{key}", true }
        };
    }

    private string ToolKey() => _toolItem != null ? _toolItem.name : "null";

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null || _toolItem == null) return false;
        if (_building.ToolStorage == null) return false;
        return StorageContainsTool(_building.ToolStorage, _toolItem);
    }

    private static bool StorageContainsTool(StorageFurniture storage, ItemSO tool)
    {
        if (storage == null || storage.ItemSlots == null) return false;
        var slots = storage.ItemSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == tool) return true;
        }
        return false;
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
        // storage doesn't expose an InteractionZone collider.
        var interactable = storage.GetComponent<InteractableObject>();
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

        // In zone — perform the take.
        var instance = TakeOneFromStorage(storage, _toolItem);
        if (instance == null)
        {
            // Race lost (another worker grabbed the tool between IsValid and Execute).
            // Fail the action; planner replans.
            _isComplete = true;
            return;
        }

        // Stamp ownership BEFORE equip so the marker survives even if the equip fails.
        instance.OwnerBuildingId = _building.BuildingId;

        // Equip in hand. If hands are already occupied, store whatever was there back
        // into the worker's bag inventory so the equip can succeed.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.IsCarrying)
        {
            ItemInstance prev = hands.DropCarriedItem();
            if (prev != null) worker.CharacterEquipment?.PickUpItem(prev);
        }
        worker.CharacterEquipment?.CarryItemInHand(instance);

        if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=cyan>[FetchTool]</color> {worker.CharacterName} fetched {_toolItem.ItemName} from {_building.name} tool storage. OwnerBuildingId={_building.BuildingId}.");
        }

        _isComplete = true;
    }

    /// <summary>
    /// Walks the storage's slots in order, removes the first instance whose ItemSO matches
    /// <paramref name="tool"/>, and returns it. Uses <see cref="StorageFurniture.RemoveItem(ItemInstance)"/>
    /// so the storage's <c>OnInventoryChanged</c> event fires and listeners (visual displays,
    /// network sync) stay in sync. Returns null when no match exists or the storage is locked.
    /// </summary>
    private static ItemInstance TakeOneFromStorage(StorageFurniture storage, ItemSO tool)
    {
        if (storage == null || tool == null || storage.ItemSlots == null) return null;
        var slots = storage.ItemSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == tool)
            {
                ItemInstance taken = slot.ItemInstance;
                if (storage.RemoveItem(taken)) return taken;
                // RemoveItem returned false (locked storage). Treat as a failed take.
                return null;
            }
        }
        return null;
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        _isComplete = false;
    }
}
