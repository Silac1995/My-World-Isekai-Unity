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

    [Tooltip("0 = use ItemSO.BasePrice")]
    public int PriceOverride;
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

    /// <summary>
    /// Resolves the effective sell price for a catalog entry: override wins when
    /// positive, otherwise the item's base price. Routes through the pure helper
    /// in MWI.Shop.Pure so the same logic is unit-testable.
    /// </summary>
    public static int ResolvePrice(ShopItemEntry entry)
    {
        int basePrice = entry.Item != null ? entry.Item.BasePrice : 0;
        return MWI.Shop.PriceResolver.Resolve(basePrice, entry.PriceOverride);
    }

    [Header("Shop Settings")]
    [Tooltip("Inspector-authored seed catalog. At runtime this is copied into the mutable _catalog list (which the management UI edits).")]
    [SerializeField] private List<ShopItemEntry> _seedCatalog = new List<ShopItemEntry>();

    private List<ShopItemEntry> _catalog;
    public IReadOnlyList<ShopItemEntry> Catalog => _catalog;

    private List<StorageFurniture> _sellShelves = new List<StorageFurniture>();
    public IReadOnlyList<StorageFurniture> SellShelves => _sellShelves;

    private List<Cashier> _cashiers = new List<Cashier>();
    public IReadOnlyList<Cashier> Cashiers => _cashiers;

    public event System.Action OnCatalogChanged;
    public event System.Action OnSellShelvesChanged;
    public event System.Action OnCashiersChanged;

    /// <inheritdoc/>
    public IEnumerable<StockTarget> GetStockTargets()
    {
        if (_catalog == null) yield break;
        foreach (var entry in _catalog)
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
    public IReadOnlyList<ShopItemEntry> ShopEntries => _catalog;

    // Cached projection of _catalog → ItemSO list. _catalog is mutable at runtime via the
    // management UI; if/when that path lands, invalidate _cachedItemsToSell on every catalog
    // mutation (and fire OnCatalogChanged). For now the cache stays valid for the lifetime
    // of the building because no runtime mutation hook is wired yet.
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
                int seedCount = _catalog?.Count ?? 0;
                _cachedItemsToSell = new List<ItemSO>(seedCount);
                if (_catalog != null)
                {
                    foreach (var entry in _catalog)
                    {
                        if (entry.Item != null) _cachedItemsToSell.Add(entry.Item);
                    }
                }
            }
            return _cachedItemsToSell;
        }
    }

    public int CustomersInQueue => _customerQueue.Count;

    /// <summary>
    /// Seeds the runtime mutable <see cref="_catalog"/> from the inspector-authored
    /// <see cref="_seedCatalog"/> on first spawn. Guarded by null-check so re-spawning on
    /// map change (or late client join) does not blow away runtime edits.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (_catalog == null)
        {
            _catalog = new List<ShopItemEntry>(_seedCatalog?.Count ?? 0);
            if (_seedCatalog != null) _catalog.AddRange(_seedCatalog);
        }
    }

    protected override void InitializeJobs()
    {
        // The shop needs a vendor at the counter.
        _jobs.Add(new JobVendor());

        // And a logistics manager to place restocking orders.
        _jobs.Add(new JobLogisticsManager("Shop Manager"));

        Debug.Log($"<color=magenta>[Shop]</color> {buildingName} initialized with 1 Vendor and 1 LogisticsManager.");
    }



    // ==========================================
    // CATALOG / CASHIER LOOKUP HELPERS
    // ==========================================

    /// <summary>
    /// O(N) scan for the catalog entry matching the given ItemSO. Returns null if
    /// not in the catalog. N is small (~50 max in practice), so a linear walk is
    /// cheaper than maintaining a dictionary mirror.
    /// </summary>
    public ShopItemEntry? GetCatalogEntry(ItemSO item)
    {
        if (item == null || _catalog == null) return null;
        for (int i = 0; i < _catalog.Count; i++)
            if (_catalog[i].Item == item) return _catalog[i];
        return null;
    }

    /// <summary>
    /// Returns the first cashier in this shop that is currently
    /// IsAvailableForCustomer (free + has a vendor if required). Order follows
    /// _cashiers list (registration order). No tie-breaking rule beyond first match.
    /// </summary>
    public Cashier GetFirstAvailableCashier()
    {
        if (_cashiers == null) return null;
        for (int i = 0; i < _cashiers.Count; i++)
        {
            var c = _cashiers[i];
            if (c != null && c.IsAvailableForCustomer) return c;
        }
        return null;
    }

    // ==========================================
    // CASHIER REGISTRATION (pool-model JobVendor)
    // ==========================================

    /// <summary>
    /// Server-only — called by Cashier.OnEnable when a cashier becomes a child of this shop.
    /// Adds a generic JobVendor slot to the pool if the cashier requires a vendor.
    /// Pool model: the slot is unbound; any vendor worker fills any open slot.
    /// </summary>
    public void RegisterCashier(Cashier cashier)
    {
        if (!IsServer) return;
        if (cashier == null || _cashiers.Contains(cashier)) return;

        _cashiers.Add(cashier);
        OnCashiersChanged?.Invoke();

        if (cashier.RequiresVendor)
        {
            _jobs.Add(new JobVendor());   // Pool model — slot is generic, not bound to cashier.
            RaiseJobsChanged();
        }
    }

    /// <summary>
    /// Server-only — called by Cashier.OnDisable. Removes the cashier from the list,
    /// and if it was vendor-requiring, removes one generic JobVendor slot from the pool.
    /// Existing fire flow handles any worker assigned to the removed slot (Unassign() before remove).
    /// </summary>
    public void UnregisterCashier(Cashier cashier)
    {
        if (!IsServer) return;
        int idx = _cashiers.IndexOf(cashier);
        if (idx < 0) return;

        _cashiers.RemoveAt(idx);
        OnCashiersChanged?.Invoke();

        if (cashier.RequiresVendor)
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (_jobs[i] is JobVendor jv)
                {
                    jv.Unassign();
                    _jobs.RemoveAt(i);
                    break;
                }
            }
            RaiseJobsChanged();
        }
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
