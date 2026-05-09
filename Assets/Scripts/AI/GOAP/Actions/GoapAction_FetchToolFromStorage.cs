using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic GOAP action: walk to ANY tool storage in the building that contains the requested
/// <paramref name="toolItem"/>, take 1 ItemInstance, stamp it with the building's BuildingId,
/// and equip it in the worker's HandsController. Composable in any worker plan that needs a
/// building-owned tool. Companion to <c>GoapAction_ReturnToolToStorage</c> (Task 4 of the
/// tool-storage plan).
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
///   - <see cref="CommercialBuilding.FindToolStorageContaining"/>(toolItem) is non-null
///     (i.e. at least one role-assigned tool storage — or the legacy singleton fallback —
///     holds an instance of toolItem)
///   - worker's hands are free (otherwise the planner picks a different prelude —
///     drop / deposit / etc. — before re-evaluating this action)
///
/// Multi-tool-storage semantics (2026-05-08):
/// The previous implementation cached <c>building.ToolStorage.GetComponent&lt;InteractableObject&gt;()</c>
/// at construction with the comment "set at design time, never swapped at runtime". That assumption
/// no longer holds with the unified storage-role system — owners can flip a storage's role at
/// runtime via the management panel. Both <c>IsValid</c> and <c>Execute</c> now resolve the
/// concrete tool storage per call via <see cref="CommercialBuilding.FindToolStorageContaining"/>,
/// which iterates every role-assigned tool storage and falls back to the legacy singleton.
/// </summary>
public class GoapAction_FetchToolFromStorage : GoapAction
{
    private readonly CommercialBuilding _building;
    private readonly ItemSO _toolItem;

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
        if (_building.FindToolStorageContaining(_toolItem) == null) return false;

        // Hands must be free at IsValid time. If they're not, the planner picks a different
        // prelude (drop / deposit / etc.) before re-evaluating this action. This keeps the
        // Execute path simple — no in-action drop/repickup dance, no silent-fail risk when
        // PickUpItem falls back to CarryItemInHand on a full bag.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree()) return false;

        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (worker == null || _building == null || _toolItem == null)
        {
            _isComplete = true;
            return;
        }

        // Resolve the storage holding the tool RIGHT NOW. With multiple role-assigned tool
        // storages, the first hit can change between IsValid and Execute (another worker took
        // the tool from one chest, leaving only the second one with stock). Re-resolve per
        // tick rather than caching at IsValid time.
        var storage = _building.FindToolStorageContaining(_toolItem);
        if (storage == null)
        {
            _isComplete = true;
            return;
        }
        var interactable = storage.GetComponent<InteractableObject>();

        // Movement gate: prefer InteractableObject.IsCharacterInInteractionZone
        // (canonical proximity API). Fall back to a flat-XZ distance check when the
        // storage doesn't expose an InteractionZone collider. Plus arrived-but-just-
        // outside softlock guard (same fix as GoapAction_FetchSeed).
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
            // Race lost (another worker grabbed the tool between IsValid and Execute, OR
            // the dropdown reassigned this storage's role between ticks).
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
            Debug.Log($"<color=cyan>[FetchTool]</color> {worker.CharacterName} fetched {_toolItem.ItemName} from {_building.name} ({storage.gameObject.name}). OwnerBuildingId={_building.BuildingId}.");
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
