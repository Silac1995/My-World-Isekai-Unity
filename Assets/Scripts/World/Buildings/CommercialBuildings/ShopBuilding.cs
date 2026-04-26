using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shop catalog entry: an item and its target stock quantity.
/// </summary>
[System.Serializable]
public struct ShopItemEntry
{
    public ItemSO Item;
    public int MaxStock;
}

/// <summary>
/// Shop-type building.
/// Requires a Vendor to sell products to customers and a LogisticsManager to restock.
/// Also manages the customer queue.
///
/// Implements <see cref="IStockProvider"/> so <see cref="BuildingLogisticsManager.CheckStockTargets"/>
/// can drive shelf restocking through the same unified path as <see cref="CraftingBuilding"/>
/// input-material restocking.
/// </summary>
public class ShopBuilding : CommercialBuilding, IStockProvider
{
    public override BuildingType BuildingType => BuildingType.Shop;

    [Header("Shop Settings")]
    [SerializeField] private List<ShopItemEntry> _itemsToSell = new List<ShopItemEntry>();

    /// <inheritdoc/>
    public IEnumerable<StockTarget> GetStockTargets()
    {
        foreach (var entry in _itemsToSell)
        {
            if (entry.Item == null) continue;
            // Preserve the existing ShopBuilding default: treat zero/negative MaxStock as 5.
            int minStock = entry.MaxStock > 0 ? entry.MaxStock : 5;
            yield return new StockTarget(entry.Item, minStock);
        }
    }
    
    [Header("Work Positions")]
    [SerializeField] private Transform _vendorPoint;
    
    public Transform VendorPoint => _vendorPoint;
    
    // Customer queue
    private Queue<Character> _customerQueue = new Queue<Character>();

    /// <summary>List of catalog entries (ItemSO + MaxStock).</summary>
    public IReadOnlyList<ShopItemEntry> ShopEntries => _itemsToSell;

    // Cached projection of _itemsToSell → ItemSO list. _itemsToSell is inspector-authored
    // and not mutated at runtime; the cache stays valid for the lifetime of the building.
    // Pre-refactor this allocated a fresh List on every property access (perf, see
    // wiki/projects/optimisation-backlog.md entry #2 / G).
    private List<ItemSO> _cachedItemsToSell;

    /// <summary>List of ItemSOs to sell (shortcut for compatibility).</summary>
    public IReadOnlyList<ItemSO> ItemsToSell
    {
        get
        {
            if (_cachedItemsToSell == null)
            {
                _cachedItemsToSell = new List<ItemSO>(_itemsToSell.Count);
                foreach (var entry in _itemsToSell)
                {
                    if (entry.Item != null) _cachedItemsToSell.Add(entry.Item);
                }
            }
            return _cachedItemsToSell;
        }
    }

    public int CustomersInQueue => _customerQueue.Count;

    protected override void InitializeJobs()
    {
        // The shop needs a vendor at the counter.
        _jobs.Add(new JobVendor());

        // And a logistics manager to place restocking orders.
        _jobs.Add(new JobLogisticsManager("Shop Manager"));

        Debug.Log($"<color=magenta>[Shop]</color> {buildingName} initialized with 1 Vendor and 1 LogisticsManager.");
    }



    /// <summary>
    /// Only the Vendor goes to their fixed station (_vendorPoint).
    /// Other employees (Manager, etc.) use the default behavior (building zone).
    /// </summary>
    public override Vector3 GetWorkPosition(Character worker)
    {
        if (worker.CharacterJob != null)
        {
            var currentJob = worker.CharacterJob.CurrentJob;

            if (currentJob is JobVendor && _vendorPoint != null)
                return _vendorPoint.position;
        }

        return base.GetWorkPosition(worker);
    }

    /// <summary>
    /// Gets the shop's logistics manager so BuyOrders can be deposited/created on it.
    /// </summary>
    public JobLogisticsManager GetLogisticsManager()
    {
        foreach (var job in _jobs)
        {
            if (job is JobLogisticsManager manager) return manager;
        }
        return null;
    }

    /// <summary>
    /// Gets the vendor of this shop.
    /// </summary>
    public JobVendor GetVendor()
    {
        foreach (var job in _jobs)
        {
            if (job is JobVendor vendor) return vendor;
        }
        return null;
    }

    /// <summary>
    /// Physically adds an item to the shop's inventory (e.g. by a Transporter).
    /// Override (not shadow) the base virtual so the base's network-count mirror stays in sync —
    /// otherwise items added through a <see cref="ShopBuilding"/>-typed reference would never
    /// appear in <see cref="CommercialBuilding.GetInventoryCountsByItemSO"/> on clients, breaking
    /// shop UI / <see cref="InteractionBuyItem"/>'s stock check on remote players.
    /// </summary>
    public override void AddToInventory(ItemInstance item)
    {
        // Defer to the base implementation: it appends to _inventory AND mirrors into the
        // replicated _inventoryItemIds NetworkList in one place.
        base.AddToInventory(item);
    }

    /// <summary>
    /// Removes and returns an item from the inventory during a sale. Routes through
    /// <see cref="CommercialBuilding.RemoveExactItemFromInventory"/> to keep the network-count
    /// mirror in sync — same reason as <see cref="AddToInventory"/>.
    /// </summary>
    public ItemInstance SellItem(ItemSO requestedItem)
    {
        var itemInstance = _inventory.Find(i => i.ItemSO == requestedItem);
        if (itemInstance != null)
        {
            // Use the base helper (which calls MirrorInventoryRemove server-side) instead of
            // _inventory.Remove(...) directly. Same observable behaviour on the host, plus the
            // replicated count drops by one on every peer.
            RemoveExactItemFromInventory(itemInstance);
            return itemInstance;
        }
        return null;
    }

    /// <summary>
    /// Checks whether the inventory contains at least one copy of the requested item.
    /// Routes through <see cref="CommercialBuilding.GetItemCount"/> so it returns the correct
    /// answer on both server and client (the underlying NetworkList is replicated).
    /// </summary>
    public bool HasItemInStock(ItemSO item)
    {
        return GetItemCount(item) > 0;
    }

    /// <summary>
    /// Returns the number of copies of this item in the inventory. Same client-safe path
    /// as <see cref="HasItemInStock"/>.
    /// </summary>
    public int GetStockCount(ItemSO item)
    {
        return GetItemCount(item);
    }

    /// <summary>
    /// Checks whether the current stock is below the desired maximum for this item.
    /// </summary>
    public bool NeedsRestock(ItemSO item, int maxStock)
    {
        return GetStockCount(item) < maxStock;
    }

    // ==========================================
    // CUSTOMER QUEUE MANAGEMENT
    // ==========================================

    /// <summary>
    /// A customer joins the shop's queue.
    /// </summary>
    public void JoinQueue(Character customer)
    {
        if (customer != null && !_customerQueue.Contains(customer))
        {
            _customerQueue.Enqueue(customer);
            Debug.Log($"<color=magenta>[Shop]</color> {customer.CharacterName} joined the queue at {buildingName}. (Waiting: {_customerQueue.Count})");
        }
    }

    /// <summary>
    /// Called by a Vendor who is ready to serve the next customer.
    /// </summary>
    public Character GetNextCustomer()
    {
        if (_customerQueue.Count > 0)
        {
            return _customerQueue.Dequeue();
        }
        return null;
    }

    /// <summary>
    /// Called when a Vendor ends their shift. If no other vendor is available,
    /// the queue is fully cleared and customers go home.
    /// </summary>
    public void ClearQueue()
    {
        if (_customerQueue.Count > 0)
        {
            Debug.Log($"<color=magenta>[Shop]</color> Shop {buildingName} is closing. {_customerQueue.Count} customers are asked to go home.");

            // For every customer in the queue we could fire an event or status change so they know
            // they should stop waiting (WaitInQueueBehaviour will handle the eviction).
            _customerQueue.Clear();
        }
    }
}
