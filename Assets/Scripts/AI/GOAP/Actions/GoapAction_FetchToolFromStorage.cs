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
///   - worker's hands are free (otherwise the planner picks a different prelude —
///     drop / deposit / etc. — before re-evaluating this action)
///
/// Notes:
///   - Movement gating uses <see cref="InteractableObject.IsCharacterInInteractionZone(Character)"/>
///     when available (per project convention), with a fallback to a flat-XZ proximity test for
///     storage furniture that doesn't expose an InteractionZone collider.
///   - <see cref="ItemInstance.OwnerBuildingId"/> is stamped BEFORE the equip call so that if a
///     future change introduces a new failure mode in equip, the ownership marker is still set
///     on the instance and the punch-out gate will catch it. Today the equip is guaranteed to
///     succeed because IsValid ensures hands are free.
/// </summary>
public class GoapAction_FetchToolFromStorage : GoapAction
{
    private readonly CommercialBuilding _building;
    private readonly ItemSO _toolItem;
    private readonly InteractableObject _storageInteractable;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    private bool _isMoving;
    private bool _isComplete;

    // Throttled stuck-state log (1 Hz). Surfaces what's blocking the action when the user
    // sees a worker frozen in front of (or away from) the tool storage.
    private float _lastStuckLogTime = -10f;

    public override string ActionName => $"FetchTool({(_toolItem != null ? _toolItem.ItemName : "?")})";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;

    public GoapAction_FetchToolFromStorage(CommercialBuilding building, ItemSO toolItem)
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

        // Hands must be free at IsValid time. If they're not, the planner picks a different
        // prelude (drop / deposit / etc.) before re-evaluating this action. This keeps the
        // Execute path simple — no in-action drop/repickup dance, no silent-fail risk when
        // PickUpItem falls back to CarryItemInHand on a full bag.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree()) return false;

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
        // storage doesn't expose an InteractionZone collider. Plus arrived-but-just-
        // outside softlock guard (same fix as GoapAction_FetchSeed).
        var interactable = _storageInteractable;
        bool inZone;
        if (interactable != null && interactable.InteractionZone != null)
        {
            inZone = interactable.IsCharacterInInteractionZone(worker);
            if (!inZone)
            {
                var movement = worker.CharacterMovement;
                bool arrived = movement == null
                    || !movement.HasPath
                    || movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
                if (arrived)
                {
                    Vector3 ip = storage.GetInteractionPosition(worker.transform.position);
                    Vector3 wp = worker.transform.position;
                    Vector3 a = new Vector3(wp.x, 0f, wp.z);
                    Vector3 b = new Vector3(ip.x, 0f, ip.z);
                    if (Vector3.Distance(a, b) <= 2f) inZone = true;
                }
            }
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

            // Stuck-state diagnostic (throttled 1 Hz). Fires every tick the worker is still
            // outside the in-zone gate. Prints carry/hand state + distance + agent path so a
            // 'frozen in front of the storage' symptom can be triaged in one log line.
            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastStuckLogTime > 1f)
            {
                _lastStuckLogTime = now;
                var hands2 = worker.CharacterVisual?.BodyPartsController?.HandsController;
                string carriedName = hands2?.CarriedItem?.ItemSO?.ItemName ?? "<none>";
                bool handsFree = hands2 != null && hands2.AreHandsFree();
                var movement = worker.CharacterMovement;
                Vector3 ip = storage.GetInteractionPosition(worker.transform.position);
                Vector3 a = new Vector3(worker.transform.position.x, 0f, worker.transform.position.z);
                Vector3 b = new Vector3(ip.x, 0f, ip.z);
                Debug.Log(
                    $"<color=orange>[FetchTool]</color> {worker.CharacterName} not in zone yet. " +
                    $"distXZ={Vector3.Distance(a, b):F2} HasPath={movement?.HasPath} " +
                    $"RemDist={movement?.RemainingDistance:F2} StopDist={movement?.StoppingDistance:F2} " +
                    $"handsFree={handsFree} carrying='{carriedName}' isMoving={_isMoving}");
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

        // Stamp ownership BEFORE equip so that if a future change introduces a new failure
        // mode in equip, the ownership marker is still set on the instance and the punch-out
        // gate will catch it. Today the equip is guaranteed to succeed because IsValid
        // ensures hands are free.
        instance.OwnerBuildingId = _building.BuildingId;

        // Hands are guaranteed free by IsValid — equip directly.
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
