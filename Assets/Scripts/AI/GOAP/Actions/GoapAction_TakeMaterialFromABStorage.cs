using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JobBuilder fetch action. Walks the AdministrativeBuilding's StorageFurniture chain to
/// find the first slot holding a material listed as missing on the active
/// <see cref="BuildOrder"/>, walks to it, takes one instance, and carries it in hand.
///
/// Modeled on <see cref="GoapAction_FetchSeed"/> — same pattern of (a) walk every
/// StorageFurniture in the building's transform tree, (b) match on ItemSO, (c) remove
/// the instance via <c>StorageFurniture.RemoveItem</c>, (d) carry via
/// <c>CharacterEquipment.CarryItemInHand</c>.
///
/// Cost = 1.0 (parity with FetchSeed / FetchToolFromStorage).
///
/// Preconditions:
///   hasMaterialsInHand = false
///   hasActiveBuildOrder = true
///   hasMatchingMaterialInABStorage = true
/// Effects:
///   hasMaterialsInHand = true
///
/// Race-loss handling: if the storage no longer holds a matching material by the time
/// we arrive (another worker took it, or logistics drained it), mark complete with no
/// effect; JobBuilder replans next tick.
/// </summary>
public class GoapAction_TakeMaterialFromABStorage : GoapAction
{
    private readonly AdministrativeBuilding _ab;

    private bool _isMoving;
    private bool _isComplete;
    private ItemSO _claimedItem;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "TakeMaterialFromABStorage";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_TakeMaterialFromABStorage(AdministrativeBuilding ab)
    {
        _ab = ab;

        _preconditions = new Dictionary<string, bool>
        {
            { "hasMaterialsInHand", false },
            { "hasActiveBuildOrder", true },
            { "hasMatchingMaterialInABStorage", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { "hasMaterialsInHand", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _ab == null) return false;

        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree()) return false;

        var order = GetActiveBuildOrder();
        if (order == null) return false;

        // At least one missing material must exist in some StorageFurniture in the AB.
        foreach (var (item, _) in order.GetMissingMaterials())
        {
            if (FindStorageContaining(_ab, item) != null) return true;
        }
        return false;
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
        if (order == null)
        {
            _isComplete = true;
            return;
        }

        // Lock onto the first missing material the AB still has in storage.
        if (_claimedItem == null)
        {
            foreach (var (item, _) in order.GetMissingMaterials())
            {
                if (FindStorageContaining(_ab, item) != null)
                {
                    _claimedItem = item;
                    break;
                }
            }
            if (_claimedItem == null)
            {
                _isComplete = true;
                return;
            }
        }

        // Re-resolve storage every tick — logistics deliveries / other workers can move
        // items between ticks. Mirrors FetchSeed.Execute's freshness guarantee.
        var storage = FindStorageContaining(_ab, _claimedItem);
        if (storage == null)
        {
            // Race lost — material disappeared from storage between IsValid and Execute,
            // or another builder grabbed it. Planner replans next tick.
            _isComplete = true;
            return;
        }

        // Movement gate: canonical proximity API (rule #36) — IsCharacterInInteractionZone
        // first, softlock guard second, flat-XZ fallback third.
        var interactable = storage.GetComponent<InteractableObject>();
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
            // Re-fire SetDestination if path is missing — rule #36 anti-freeze pattern.
            var movement = worker.CharacterMovement;
            if (!_isMoving || (movement != null && !movement.HasPath))
            {
                Vector3 target = storage.GetInteractionPosition(worker.transform.position);
                movement?.SetDestination(target);
                _isMoving = true;
            }
            return;
        }

        // In zone — take one instance.
        var instance = TakeOneFromStorage(storage, _claimedItem);
        if (instance == null)
        {
            _isComplete = true;
            return;
        }

        worker.CharacterEquipment?.CarryItemInHand(instance);

        if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=cyan>[TakeMaterialFromABStorage]</color> {worker.CharacterName} took {_claimedItem.ItemName} from {storage.FurnitureName} for BuildOrder '{order.QuestId}'.");
        }

        _isComplete = true;
    }

    private BuildOrder GetActiveBuildOrder()
    {
        if (_ab == null) return null;
        var blm = _ab.LogisticsManager;
        return blm != null ? blm.GetFirstActiveBuildOrder() : null;
    }

    /// <summary>
    /// Walks every <see cref="StorageFurniture"/> in the building's transform tree and
    /// returns the first one containing an instance of <paramref name="target"/>. Mirrors
    /// <see cref="GoapAction_FetchSeed.FindStorageContaining"/> verbatim — the fetch side
    /// is intentionally "find it wherever it is"; the deposit-routing layer is what
    /// enforces role-based placement (tool drawer for tools, sell shelf for catalog
    /// items, etc.). For builder materials, every chest in the AB is a valid source.
    /// </summary>
    private static StorageFurniture FindStorageContaining(CommercialBuilding building, ItemSO target)
    {
        if (building == null || target == null) return null;
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
                    return sf;
            }
        }
        return null;
    }

    private static ItemInstance TakeOneFromStorage(StorageFurniture storage, ItemSO target)
    {
        if (storage == null || target == null) return null;
        var slots = storage.ItemSlots;
        if (slots == null) return null;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == target)
            {
                var taken = slot.ItemInstance;
                if (storage.RemoveItem(taken)) return taken;
                return null;
            }
        }
        return null;
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        _isComplete = false;
        _claimedItem = null;
    }
}
