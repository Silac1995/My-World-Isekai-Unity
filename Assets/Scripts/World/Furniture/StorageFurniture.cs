using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Storage furniture (chest, shelf, barrel, wardrobe...) — slot-based container that
/// mirrors the player Inventory pattern: a flat list of typed ItemSlots authored
/// per-prefab through capacity ints. Renderers live on optional companion components
/// (e.g. <see cref="StorageVisualDisplay"/> for shelves); this class is pure data.
/// </summary>
public class StorageFurniture : Furniture
{
    [Header("Storage Capacity")]
    [Tooltip("Slots that accept anything except weapons (matches Inventory MiscSlot semantics — wearables fit too).")]
    [SerializeField] private int _miscCapacity = 8;
    [Tooltip("Slots that accept only weapons.")]
    [SerializeField] private int _weaponCapacity = 0;
    [Tooltip("Slots that accept only wearables (helmet, chest, pants, bag, etc.).")]
    [SerializeField] private int _wearableCapacity = 0;
    [Tooltip("Slots that accept any item — generic catch-all.")]
    [SerializeField] private int _anyCapacity = 0;

    [Header("Storage State")]
    [SerializeField] private bool _isLocked = false;

    private List<ItemSlot> _itemSlots;

    /// <summary>Fired whenever slot contents (or lock state) change.</summary>
    public event Action OnInventoryChanged;

    public IReadOnlyList<ItemSlot> ItemSlots => _itemSlots;
    public int Capacity => _itemSlots?.Count ?? 0;
    public bool IsLocked => _isLocked;

    public bool IsFull
    {
        get
        {
            if (_itemSlots == null) return false;
            for (int i = 0; i < _itemSlots.Count; i++)
                if (_itemSlots[i].IsEmpty()) return false;
            return true;
        }
    }

    // Strict-first slot-type priority used by AddItem so dedicated slots fill before generic ones.
    private static readonly Type[] _wearablePriority = { typeof(WearableSlot), typeof(MiscSlot), typeof(AnySlot) };
    private static readonly Type[] _weaponPriority = { typeof(WeaponSlot), typeof(AnySlot) };
    private static readonly Type[] _miscPriority = { typeof(MiscSlot), typeof(AnySlot) };

    protected virtual void Awake()
    {
        InitializeItemSlots();
    }

    private void InitializeItemSlots()
    {
        int total = _miscCapacity + _weaponCapacity + _wearableCapacity + _anyCapacity;
        _itemSlots = new List<ItemSlot>(total);

        for (int i = 0; i < _miscCapacity; i++) _itemSlots.Add(new MiscSlot());
        for (int i = 0; i < _weaponCapacity; i++) _itemSlots.Add(new WeaponSlot());
        for (int i = 0; i < _wearableCapacity; i++) _itemSlots.Add(new WearableSlot());
        for (int i = 0; i < _anyCapacity; i++) _itemSlots.Add(new AnySlot());
    }

    public void Lock()
    {
        if (_isLocked) return;
        _isLocked = true;
        OnInventoryChanged?.Invoke();
    }

    public void Unlock()
    {
        if (!_isLocked) return;
        _isLocked = false;
        OnInventoryChanged?.Invoke();
    }

    public bool HasFreeSpaceForItem(ItemInstance item)
    {
        if (item == null || _itemSlots == null) return false;
        for (int i = 0; i < _itemSlots.Count; i++)
        {
            var slot = _itemSlots[i];
            if (slot.IsEmpty() && slot.CanAcceptItem(item)) return true;
        }
        return false;
    }

    public bool HasFreeSpaceForWeapon() => HasEmptyOfType(typeof(WeaponSlot)) || HasEmptyOfType(typeof(AnySlot));
    public bool HasFreeSpaceForMisc() => HasEmptyOfType(typeof(MiscSlot)) || HasEmptyOfType(typeof(AnySlot));
    public bool HasFreeSpaceForWearable() =>
        HasEmptyOfType(typeof(WearableSlot)) || HasEmptyOfType(typeof(MiscSlot)) || HasEmptyOfType(typeof(AnySlot));
    public bool HasFreeSpaceForAny() => HasEmptyOfType(typeof(AnySlot));

    public bool HasFreeSpaceForItemSO(ItemSO itemSO)
    {
        if (itemSO == null) return false;
        if (itemSO is WeaponSO) return HasFreeSpaceForWeapon();
        if (itemSO is WearableSO) return HasFreeSpaceForWearable();
        return HasFreeSpaceForMisc();
    }

    /// <summary>
    /// Adds the item to the first compatible slot, preferring strictly-typed slots
    /// before falling back to the generic AnySlot catch-all. Returns false when locked,
    /// when the item is null, or when no compatible slot is available.
    /// </summary>
    public bool AddItem(ItemInstance item)
    {
        if (item == null || _itemSlots == null || _isLocked) return false;

        Type[] priority;
        if (item is WeaponInstance) priority = _weaponPriority;
        else if (item is WearableInstance) priority = _wearablePriority;
        else priority = _miscPriority;

        for (int p = 0; p < priority.Length; p++)
        {
            var slotType = priority[p];
            for (int i = 0; i < _itemSlots.Count; i++)
            {
                var slot = _itemSlots[i];
                if (slot.GetType() == slotType && slot.IsEmpty() && slot.CanAcceptItem(item))
                {
                    slot.ItemInstance = item;
                    item.IsNewlyAdded = true;
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }
        }
        return false;
    }

    public bool RemoveItem(ItemInstance item)
    {
        if (item == null || _itemSlots == null || _isLocked) return false;
        for (int i = 0; i < _itemSlots.Count; i++)
        {
            var slot = _itemSlots[i];
            if (slot.ItemInstance == item)
            {
                slot.ClearSlot();
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void RemoveItemFromSlot(ItemSlot slot)
    {
        if (slot == null || _itemSlots == null || _isLocked) return;
        if (slot.IsEmpty()) return;
        if (!_itemSlots.Contains(slot)) return;
        slot.ClearSlot();
        OnInventoryChanged?.Invoke();
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (_itemSlots == null) return null;
        if (index < 0 || index >= _itemSlots.Count) return null;
        return _itemSlots[index];
    }

    private bool HasEmptyOfType(Type slotType)
    {
        if (_itemSlots == null) return false;
        for (int i = 0; i < _itemSlots.Count; i++)
        {
            var slot = _itemSlots[i];
            if (slot.GetType() == slotType && slot.IsEmpty()) return true;
        }
        return false;
    }

    /// <summary>
    /// **Network sync API — call only from <see cref="StorageFurnitureNetworkSync"/>.**
    ///
    /// Replaces the current contents of all slots with the supplied entries:
    /// every slot is cleared, then each <c>(slotIndex, instance)</c> entry is
    /// written into the matching slot. Bypasses strict-first slot priority —
    /// that logic only runs on the server-side <see cref="AddItem"/>; clients
    /// just mirror the resulting layout. Fires <see cref="OnInventoryChanged"/>
    /// exactly once after the rewrite so visual displays rebuild on this peer.
    ///
    /// **Slot compatibility.** The server has already validated each item
    /// against its destination slot via <see cref="ItemSlot.CanAcceptItem"/>
    /// at the time of insertion. When clients call this method with the
    /// server-authored entries, the same <c>CanAcceptItem</c> check runs again
    /// inside <see cref="ItemSlot.ItemInstance"/>'s setter — which is fine
    /// because the deserialized instance is recreated through
    /// <c>ItemSO.CreateInstance()</c>, preserving the runtime type
    /// (<see cref="WeaponInstance"/> / <see cref="WearableInstance"/> / …)
    /// and therefore passing the same gate the server passed.
    ///
    /// **Locked storage:** unlike <see cref="AddItem"/>, this method ignores
    /// <see cref="_isLocked"/> — clients always mirror server state regardless
    /// of lock. Lock state itself is intentionally NOT yet replicated; if/when
    /// it becomes a network-visible concern, extend the sync component, not
    /// this method.
    /// </summary>
    /// <param name="entries">Sparse list of (slotIndex, instance) pairs. Slot
    /// indices not present in the list are treated as empty.</param>
    public void ApplySyncedSlotsFromNetwork(IReadOnlyList<(int slotIndex, ItemInstance instance)> entries)
    {
        if (_itemSlots == null) return;

        // Clear every slot first — sparse representation means absent indices
        // mean "empty", so we must wipe before applying.
        for (int i = 0; i < _itemSlots.Count; i++)
        {
            _itemSlots[i].ClearSlot();
        }

        if (entries != null)
        {
            for (int e = 0; e < entries.Count; e++)
            {
                var (slotIndex, instance) = entries[e];
                if (slotIndex < 0 || slotIndex >= _itemSlots.Count) continue;
                if (instance == null) continue;
                _itemSlots[slotIndex].ItemInstance = instance;
            }
        }

        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// **Save-restore API — server-only. Call only from
    /// <c>MapController.SpawnSavedBuildings</c> / <c>MapController.WakeUp</c>
    /// after the building's default-furniture spawn has finished.**
    ///
    /// Functionally identical to <see cref="ApplySyncedSlotsFromNetwork"/>:
    /// clears every slot then writes each <c>(slotIndex, instance)</c> entry
    /// into the matching slot, ignoring <see cref="_isLocked"/>, and fires
    /// <see cref="OnInventoryChanged"/> exactly once at the end.
    ///
    /// **Why a separate method instead of just calling <c>ApplySyncedSlotsFromNetwork</c>:**
    /// the network-sync method's contract is "called only by the sync component";
    /// keeping save-restore on its own named entry point makes call-site grep
    /// clean, lets future authors add save-only logging or telemetry without
    /// polluting the network path, and makes the OWNERship of each call site
    /// obvious in stack traces.
    ///
    /// **Network propagation:** the <see cref="OnInventoryChanged"/> fired here
    /// is exactly the same event the sync component subscribed to in its
    /// server-side <c>OnNetworkSpawn</c>. The sync component's handler runs
    /// <c>RebuildNetworkListFromStorage</c> and rewrites the replicated
    /// <c>NetworkList</c> — so late-joining clients automatically see the
    /// restored contents on connect, with no extra restore-side networking work.
    /// </summary>
    /// <param name="entries">Sparse list of (slotIndex, instance) pairs. Slot
    /// indices not present in the list are treated as empty.</param>
    public void RestoreFromSaveData(IReadOnlyList<(int slotIndex, ItemInstance instance)> entries)
    {
        if (_itemSlots == null) return;

        for (int i = 0; i < _itemSlots.Count; i++)
        {
            _itemSlots[i].ClearSlot();
        }

        if (entries != null)
        {
            for (int e = 0; e < entries.Count; e++)
            {
                var (slotIndex, instance) = entries[e];
                if (slotIndex < 0 || slotIndex >= _itemSlots.Count) continue;
                if (instance == null) continue;
                _itemSlots[slotIndex].ItemInstance = instance;
            }
        }

        OnInventoryChanged?.Invoke();
    }
}
