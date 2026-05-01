using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;

/// <summary>
/// Specialised fetch for the Farmer plan: walks to a building storage furniture containing
/// a <see cref="SeedSO"/> matching an unclaimed <c>PlantCropTask</c>, takes 1 instance,
/// equips in the worker's hand. Companion to <c>GoapAction_PlantCrop</c>.
///
/// Differs from <see cref="GoapAction_FetchToolFromStorage"/> (Plan 1):
/// - Target storage is the building's general inventory (any <c>StorageFurniture</c> in the
///   building's transform tree), NOT the dedicated <c>_toolStorageFurniture</c>. Seeds are
///   ordinary stockable inputs that the logistics chain delivers into the building's
///   shelves; the tool storage primitive is reserved for tool-loop items (watering can,
///   axe, …).
/// - Does NOT stamp <see cref="ItemInstance.OwnerBuildingId"/>. Seeds are consumables —
///   <see cref="MWI.Farming.CharacterAction_PlaceCrop"/> calls
///   <c>HandsController.ClearCarriedItem()</c> on plant, so the instance disappears. The
///   ownership marker only matters for tools that must be returned.
/// - <see cref="IsValid"/> gates on (hands free) AND (an unclaimed <c>PlantCropTask</c>
///   exists whose crop has a seed in the building's inventory) — the planner's effects
///   chain to <c>GoapAction_PlantCrop</c>'s <c>hasSeedInHand=true</c> precondition.
///
/// Cost = 1 (parity with FetchToolFromStorage and other low-cost prelude actions).
///
/// Preconditions:
///   hasSeedInHand = false
///   hasUnfilledPlantTask = true
///   hasMatchingSeedInStorage = true
/// Effects:
///   hasSeedInHand = true
///
/// Race-loss handling: <see cref="Execute"/> claims a crop on the first tick by walking
/// the building's available task list. If the storage no longer holds a matching seed by
/// the time we arrive (another worker took it, or the slot was drained), we mark complete
/// with no effect; the planner re-plans on the next tick and either picks a different
/// crop or chooses a different action.
/// </summary>
public class GoapAction_FetchSeed : GoapAction
{
    private readonly FarmingBuilding _building;

    private bool _isMoving;
    private bool _isComplete;
    private CropSO _claimedCrop;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "FetchSeed";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_FetchSeed(FarmingBuilding building)
    {
        _building = building;

        _preconditions = new Dictionary<string, bool>
        {
            { "hasSeedInHand", false },
            { "hasUnfilledPlantTask", true },
            { "hasMatchingSeedInStorage", true }
        };

        _effects = new Dictionary<string, bool>
        {
            { "hasSeedInHand", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null) return false;
        if (_building.TaskManager == null) return false;

        // Hands must be free at IsValid time. Same rationale as
        // GoapAction_FetchToolFromStorage — the planner picks a different prelude (drop /
        // deposit / etc.) before re-evaluating this action when hands are full, so the
        // Execute path stays simple.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree()) return false;

        // At least one actionable PlantCropTask whose crop has a seed in storage. "Actionable" =
        // unclaimed (in AvailableTasks) OR already claimed by this worker via the auto-claim path
        // (in InProgressTasks). Without the InProgressTasks branch, the planner sees nothing to
        // do the moment WorkerStartingShift's TryAutoClaimExistingQuests sweeps every published
        // PlantCropTask onto the worker's CharacterQuestLog (and therefore into _inProgressTasks).
        var available = _building.TaskManager.AvailableTasks;
        for (int i = 0; i < available.Count; i++)
        {
            if (available[i] is PlantCropTask pct && pct.Crop != null && BuildingHasSeedFor(pct.Crop))
                return true;
        }
        var inProgress = _building.TaskManager.InProgressTasks;
        for (int i = 0; i < inProgress.Count; i++)
        {
            if (inProgress[i] is PlantCropTask pct
                && pct.Crop != null
                && pct.ClaimedByWorkers.Contains(worker)
                && BuildingHasSeedFor(pct.Crop))
                return true;
        }
        return false;
    }

    private bool BuildingHasSeedFor(CropSO crop)
    {
        var seed = ResolveSeedFor(crop);
        if (seed == null) return false;
        // Reuse the same storage-walk used in Execute: a seed counts as available if any
        // child StorageFurniture (excluding ToolStorage) holds at least one instance.
        // Reading only the building's logical _inventory misses seeds the player or
        // logistics chain dropped into a chest, so IsValid would falsely fail and the
        // planner would filter this action out → JobFarmer picks Idle.
        return FindStorageContaining(_building, seed) != null;
    }

    private static SeedSO ResolveSeedFor(CropSO crop)
    {
        if (crop == null || crop.HarvestOutputs == null) return null;
        for (int j = 0; j < crop.HarvestOutputs.Count; j++)
        {
            var entry = crop.HarvestOutputs[j];
            if (entry.Item is SeedSO seedSO && seedSO.CropToPlant == crop)
                return seedSO;
        }
        return null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (worker == null || _building == null || _building.TaskManager == null)
        {
            _isComplete = true;
            return;
        }

        // Claim a target crop on first tick. We don't claim the PlantCropTask itself here —
        // GoapAction_PlantCrop does that once we arrive at the cell. This keeps
        // FetchSeed cheap (no quest churn) and lets the planner replan freely if the
        // race is lost between fetch and plant. Walks BOTH _availableTasks AND the
        // worker's already-claimed in-progress tasks (the auto-claim path moves them
        // out of _availableTasks the moment they're published — same fix as IsValid).
        if (_claimedCrop == null)
        {
            var available = _building.TaskManager.AvailableTasks;
            for (int i = 0; i < available.Count; i++)
            {
                if (available[i] is PlantCropTask pct && pct.Crop != null && BuildingHasSeedFor(pct.Crop))
                {
                    _claimedCrop = pct.Crop;
                    break;
                }
            }
            if (_claimedCrop == null)
            {
                var inProgress = _building.TaskManager.InProgressTasks;
                for (int i = 0; i < inProgress.Count; i++)
                {
                    if (inProgress[i] is PlantCropTask pct
                        && pct.Crop != null
                        && pct.ClaimedByWorkers.Contains(worker)
                        && BuildingHasSeedFor(pct.Crop))
                    {
                        _claimedCrop = pct.Crop;
                        break;
                    }
                }
            }
            if (_claimedCrop == null)
            {
                _isComplete = true;
                return;
            }
        }

        var seedSO = ResolveSeedFor(_claimedCrop);
        if (seedSO == null)
        {
            _isComplete = true;
            return;
        }

        // Find a storage in the building containing the seed. Done every tick because
        // logistics deliveries / other workers can shuffle the inventory between ticks.
        var storage = FindStorageContaining(_building, seedSO);
        if (storage == null)
        {
            // Race lost — seed disappeared from storage between IsValid and Execute, or
            // moved to a slot we already passed. Planner replans.
            _isComplete = true;
            return;
        }

        // Movement gate: prefer InteractableObject.IsCharacterInInteractionZone (canonical
        // proximity API per project rules), fall back to flat-XZ distance for storage that
        // doesn't expose an InteractionZone collider.
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

        // In zone — take 1 seed.
        var instance = TakeOneFromStorage(storage, seedSO);
        if (instance == null)
        {
            _isComplete = true;
            return;
        }

        // No OwnerBuildingId stamp — seeds are consumed on plant by CharacterAction_PlaceCrop.
        worker.CharacterEquipment?.CarryItemInHand(instance);

        if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=cyan>[FetchSeed]</color> {worker.CharacterName} fetched {seedSO.ItemName} for crop {_claimedCrop.Id} from {_building.name}.");
        }

        _isComplete = true;
    }

    /// <summary>
    /// Walks every <see cref="StorageFurniture"/> in the building's transform tree and
    /// returns the first one containing an instance of <paramref name="target"/>. Walks
    /// ALL chests including the one mapped to <see cref="CommercialBuilding.ToolStorage"/>:
    /// the deposit-routing layer (CommercialBuilding.FindStorageFurnitureForItem +
    /// GoapAction_GatherStorageItems.DetermineStoragePosition) is what reserves the tool
    /// drawer for tools by SKIPPING it for non-tool deposits — the FETCH side does not
    /// care, it just needs to find the seed wherever it physically is. Without this,
    /// designs with a single crate (which becomes ToolStorage by the first-crate
    /// fallback) had every seed silently invisible to the planner → JobFarmer Idle.
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
        _claimedCrop = null;
    }
}
