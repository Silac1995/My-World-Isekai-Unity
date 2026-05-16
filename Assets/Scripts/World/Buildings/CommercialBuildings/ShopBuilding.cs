using System.Collections.Generic;
using Unity.Netcode;
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

    /// <summary>
    /// Customer-facing sell-shelves. Wraps the unified storage-role system —
    /// a sell-shelf is just a <see cref="StorageFurniture"/> with
    /// <see cref="StorageFurniture.Role"/> == <see cref="StorageRoleType.SellShelf"/>.
    /// Phase 2c (2026-05-08) refactor: dropped the dedicated <c>_sellShelves</c>
    /// list + <c>SetSellShelfFlagServerRpc</c>. Owner-driven assignment now goes
    /// through <see cref="CommercialBuilding.TrySetStorageRoleServerRpc"/>;
    /// changes fire <see cref="CommercialBuilding.OnStorageRolesChanged"/>.
    /// Old saves' <c>SellShelfFurnitureKeys</c> migrate into role assignments
    /// during <see cref="OnFurnituresLoaded"/>.
    /// </summary>
    public IReadOnlyList<StorageFurniture> SellShelves => GetStoragesWithRole(StorageRoleType.SellShelf);

    /// <inheritdoc/>
    public override System.Collections.Generic.IReadOnlyList<StorageRoleDescriptor> SupportedStorageRoles
        => StorageRoleCatalog.Shop;

    private List<Cashier> _cashiers = new List<Cashier>();
    public IReadOnlyList<Cashier> Cashiers => _cashiers;

    private ShopBuildingNetSync _netSync;
    public ShopBuildingNetSync NetSync => _netSync;

    public event System.Action OnCatalogChanged;
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

    /// <summary>
    /// Seeds the runtime mutable <see cref="_catalog"/> from the inspector-authored
    /// <see cref="_seedCatalog"/> on first spawn. Guarded by null-check so re-spawning on
    /// map change (or late client join) does not blow away runtime edits.
    ///
    /// Server-only late-bind for <see cref="Cashier"/> children (added 2026-05-08): the
    /// <c>Building._defaultFurnitureLayout</c> spawn pipeline uses <c>Instantiate</c> +
    /// <c>SetParent</c> as separate calls, so <see cref="Cashier.Awake"/> runs BEFORE
    /// the parent transform is set and <c>GetComponentInParent&lt;CommercialBuilding&gt;</c>
    /// returns null. <see cref="Cashier.OnEnable"/> sees <c>LinkedShop == null</c> and
    /// silently no-ops — meaning a designer-authored Cashier inside a Shop prefab never
    /// registered, and the <see cref="JobVendor"/> pool slot never appeared.
    ///
    /// We close that race by walking the Cashier descendants once the building's
    /// <c>NetworkObject</c> is server-spawned (so <c>IsServer</c> is true and
    /// <see cref="RegisterCashier"/>'s gate passes) and calling
    /// <see cref="Cashier.TryRegisterWithShop"/> on each. Idempotent — already-registered
    /// cashiers short-circuit via the <c>_cashiers.Contains</c> guard.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _netSync = GetComponent<ShopBuildingNetSync>();
        if (_catalog == null)
        {
            _catalog = new List<ShopItemEntry>(_seedCatalog?.Count ?? 0);
            if (_seedCatalog != null) _catalog.AddRange(_seedCatalog);
        }

        if (IsServer)
        {
            var cashierChildren = GetComponentsInChildren<Cashier>(includeInactive: true);
            for (int i = 0; i < cashierChildren.Length; i++)
            {
                var c = cashierChildren[i];
                if (c == null) continue;
                c.TryRegisterWithShop();
            }
        }
    }

    protected override void InitializeJobs()
    {
        // Vendor slots are now added dynamically by RegisterCashier (Task 13) when
        // cashiers are placed. No static JobVendor is added here.

        // The shop still needs a logistics manager to place restocking orders.
        _jobs.Add(new JobLogisticsManager("Shop Manager"));

        Debug.Log($"<color=magenta>[Shop]</color> {BuildingName} initialized with 1 LogisticsManager. Vendor slots added dynamically per cashier.");
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
    /// Snapshot of all currently-active JobVendor slots in this shop. NOT cached —
    /// used only by debug UI / management panel; if it becomes hot, cache via
    /// dirty flag (rule #34).
    /// </summary>
    public IEnumerable<JobVendor> Vendors
    {
        get
        {
            for (int i = 0; i < _jobs.Count; i++)
                if (_jobs[i] is JobVendor jv) yield return jv;
        }
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

    // Customer-queue / vendor-point surfaces removed in Phase 2b Task 15.
    // Cashier-driven flow replaces them: customers route through CashierInteractable
    // and bind to a per-cashier customer lock instead of a shop-level queue.

    // ============================================================================
    // SERVER RPCs - owner-only mutations
    // ============================================================================

    [ServerRpc(RequireOwnership = false)]
    public void AddCatalogEntryServerRpc(string itemId, int maxStock, int priceOverride, ServerRpcParams p = default)
    {
        if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
        DoAddCatalogEntry(itemId, maxStock, priceOverride);
    }

    /// <summary>
    /// Shared implementation of <see cref="AddCatalogEntryServerRpc"/> minus the
    /// <see cref="ValidateOwnerCaller"/> auth gate. Called both from the production
    /// RPC (after the gate) and from <c>DevForceAddCatalogEntry</c> (host-only,
    /// gated by DevModeManager). Bit-for-bit identical to the original RPC body.
    /// </summary>
    private void DoAddCatalogEntry(string itemId, int maxStock, int priceOverride)
    {
        var so = ResolveItemSO(itemId);
        if (so == null) { Debug.LogWarning($"[Shop] AddCatalogEntry: unknown itemId '{itemId}'"); return; }
        if (maxStock < 0) maxStock = 0;
        if (priceOverride < 0) priceOverride = 0;

        if (GetCatalogEntry(so) != null) return; // duplicate - silently ignore
        var entry = new ShopItemEntry { Item = so, MaxStock = maxStock, PriceOverride = priceOverride };
        _catalog.Add(entry);
        _netSync.PushCatalogEntryAddedServer(entry);
        OnCatalogChanged?.Invoke();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RemoveCatalogEntryServerRpc(string itemId, ServerRpcParams p = default)
    {
        if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
        DoRemoveCatalogEntry(itemId);
    }

    /// <summary>Shared impl of <see cref="RemoveCatalogEntryServerRpc"/> minus auth. See <see cref="DoAddCatalogEntry"/>.</summary>
    private void DoRemoveCatalogEntry(string itemId)
    {
        for (int i = _catalog.Count - 1; i >= 0; i--)
        {
            if (_catalog[i].Item != null && _catalog[i].Item.ItemId == itemId)
            {
                _catalog.RemoveAt(i);
                _netSync.PushCatalogEntryRemovedServer(itemId);
                OnCatalogChanged?.Invoke();
                return;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void EditCatalogEntryServerRpc(string itemId, int newMaxStock, int newPriceOverride, ServerRpcParams p = default)
    {
        if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
        DoEditCatalogEntry(itemId, newMaxStock, newPriceOverride);
    }

    /// <summary>Shared impl of <see cref="EditCatalogEntryServerRpc"/> minus auth. See <see cref="DoAddCatalogEntry"/>.</summary>
    private void DoEditCatalogEntry(string itemId, int newMaxStock, int newPriceOverride)
    {
        if (newMaxStock < 0) newMaxStock = 0;
        if (newPriceOverride < 0) newPriceOverride = 0;
        for (int i = 0; i < _catalog.Count; i++)
        {
            if (_catalog[i].Item != null && _catalog[i].Item.ItemId == itemId)
            {
                var e = _catalog[i];
                e.MaxStock = newMaxStock;
                e.PriceOverride = newPriceOverride;
                _catalog[i] = e;
                _netSync.PushCatalogEntryEditedServer(e);
                OnCatalogChanged?.Invoke();
                return;
            }
        }
    }

    // SetSellShelfFlagServerRpc deleted in the 2026-05-08 unified storage-role refactor.
    // Owner-side assignment now goes through CommercialBuilding.TrySetStorageRoleServerRpc.
    // The Storages tab dropdown is the canonical UI surface; the old per-shelf checkbox
    // tab + its row are retired (see wiki/projects/management-panel-followups.md §1).
    // Dev-mode override path: see CommercialBuilding.DevForceSetStorageRole.

    [ServerRpc(RequireOwnership = false)]
    public void WithdrawCashierTillServerRpc(NetworkObjectReference cashierRef, ServerRpcParams p = default)
    {
        if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
        // Production path: deposit into the calling owner's wallet.
        var recipient = ResolveCharacterFromClientId(p.Receive.SenderClientId);
        DoWithdrawCashierTill(cashierRef, recipient);
    }

    /// <summary>
    /// Shared impl of <see cref="WithdrawCashierTillServerRpc"/> minus auth. The
    /// dev path passes an explicit recipient (typically the host's local Character)
    /// instead of resolving from <c>SenderClientId</c>. See <see cref="DoAddCatalogEntry"/>.
    /// </summary>
    private void DoWithdrawCashierTill(NetworkObjectReference cashierRef, Character recipient)
    {
        if (!cashierRef.TryGet(out NetworkObject cashierObj)) return;
        var cashier = cashierObj.GetComponent<Cashier>();
        if (cashier == null) return;

        var currency = MWI.Economy.CurrencyId.Default;
        int balance = cashier.GetTillBalance(currency);
        if (balance <= 0) return;
        if (!cashier.DebitTill(currency, balance, "OwnerWithdraw")) return;

        recipient?.CharacterWallet?.AddCoins(currency, balance, $"FromCashier_{cashier.FurnitureName}");
    }

    public override System.Collections.Generic.IReadOnlyList<MWI.UI.Management.IManagementTab> GetManagementTabs()
    {
        // Base now provides Hiring + Storages (the unified storage-role tab — Sell-Shelf
        // is one option in its dropdown for ShopBuilding via SupportedStorageRoles).
        // Phase 2c (2026-05-08) dropped the dedicated ShopShelvesTab.
        var tabs = new System.Collections.Generic.List<MWI.UI.Management.IManagementTab>(base.GetManagementTabs());
        tabs.Add(new MWI.UI.Management.ShopCatalogTab(this));
        tabs.Add(new MWI.UI.Management.ShopCashiersTab(this));
        return tabs;
    }

    // ----- Helpers -----

    private bool ValidateOwnerCaller(ServerRpcParams p)
    {
        var caller = ResolveCharacterFromClientId(p.Receive.SenderClientId);
        if (caller == null) return false;
        if (Owner == null || caller != Owner)
        {
            Debug.LogWarning($"[Shop] {BuildingName}: rejected ServerRpc - caller {caller.CharacterName} is not owner ({Owner?.CharacterName ?? "null"}).");
            return false;
        }
        return true;
    }

    private static ClientRpcParams SingleClientRpcParams(ServerRpcParams source) => new()
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { source.Receive.SenderClientId } }
    };

    private Character ResolveCharacterFromClientId(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return null;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var nc)) return null;
        return nc?.PlayerObject?.GetComponent<Character>();
    }

    private static ItemSO ResolveItemSO(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        var all = Resources.LoadAll<ItemSO>("Data/Item");
        return System.Array.Find(all, x => x.ItemId == itemId);
    }

    // ============================================================================
    // SAVE / LOAD — Shop-specific restore
    // ----------------------------------------------------------------------------
    // The base Building / CommercialBuilding hierarchy already exposes two unrelated
    // RestoreFromSaveData overloads (construction state, owner+employees). Adding a
    // third overload here would shadow Building.RestoreFromSaveData(BuildingSaveData)
    // and risk silent base-call drops. Distinct method names instead — MapController
    // calls them explicitly in the documented order.
    //
    // Catalog restore is immediate (no live-furniture dependency).
    // Sell-shelf restore is deferred via _pendingSellShelfKeys + OnFurnituresLoaded()
    // because shelves are matched against live StorageFurniture instances which only
    // exist after the building's default-furniture spawn pass has finished.
    // ============================================================================

    /// <summary>
    /// Pending shelf keys captured during <see cref="RestoreShopFromSaveData"/>; consumed
    /// by <see cref="OnFurnituresLoaded"/> once the live <see cref="StorageFurniture"/>
    /// instances spawned by the building's default-furniture pass exist on the hierarchy.
    /// Server-only state (clients never run the restore path).
    /// </summary>
    private System.Collections.Generic.List<string> _pendingSellShelfKeys;

    /// <summary>
    /// Server-only. Restores the runtime mutable <see cref="_catalog"/> from
    /// <paramref name="rawData"/>, then stages the saved sell-shelf keys for deferred
    /// resolution via <see cref="OnFurnituresLoaded"/>.
    ///
    /// <para>
    /// Call AFTER <c>NetworkObject.Spawn</c> on the building (so <see cref="OnNetworkSpawn"/>
    /// has seeded <see cref="_catalog"/> from the inspector seed) and AFTER the building's
    /// default-furniture spawn (so live <see cref="StorageFurniture"/> children exist by
    /// the time <see cref="OnFurnituresLoaded"/> runs).
    /// </para>
    ///
    /// <para>
    /// Catalog restore overwrites the in-memory list completely — the seed populated by
    /// <see cref="OnNetworkSpawn"/> is intentionally discarded so saved owner edits win
    /// over the prefab default. Resolves each <c>itemId</c> via the standard
    /// <c>Resources.LoadAll&lt;ItemSO&gt;("Data/Item")</c> lookup; entries whose itemId
    /// no longer resolves are dropped with a warning (rule #31 — graceful degradation).
    /// </para>
    /// </summary>
    public void RestoreShopFromSaveData(MWI.WorldSystem.BuildingSaveData rawData)
    {
        if (!IsServer) return;
        if (rawData == null) return;

        // --- Catalog ---
        try
        {
            if (_catalog == null) _catalog = new System.Collections.Generic.List<ShopItemEntry>(rawData.ShopCatalog?.Count ?? 0);
            else _catalog.Clear();

            // Invalidate the cached ItemSO projection — it is rebuilt lazily from _catalog.
            _cachedItemsToSell = null;

            if (rawData.ShopCatalog != null)
            {
                foreach (var saved in rawData.ShopCatalog)
                {
                    var so = ResolveItemSO(saved.itemId);
                    if (so == null)
                    {
                        Debug.LogWarning($"<color=orange>[ShopBuilding:Restore]</color> {BuildingName}: catalog entry itemId='{saved.itemId}' did not resolve to an ItemSO — entry dropped.");
                        continue;
                    }
                    _catalog.Add(new ShopItemEntry
                    {
                        Item = so,
                        MaxStock = saved.maxStock,
                        PriceOverride = saved.priceOverride
                    });
                }
            }

            OnCatalogChanged?.Invoke();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[ShopBuilding:Restore]</color> {BuildingName}: catalog restore failed — leaving seeded catalog in place.");
        }

        // --- Sell-shelves: defer resolution until live StorageFurniture children exist ---
        if (rawData.SellShelfFurnitureKeys != null && rawData.SellShelfFurnitureKeys.Count > 0)
        {
            _pendingSellShelfKeys = new System.Collections.Generic.List<string>(rawData.SellShelfFurnitureKeys);
        }
        else
        {
            _pendingSellShelfKeys = null;
        }
    }

    /// <summary>
    /// Server-only. Resolves the deferred sell-shelf keys from a prior
    /// <see cref="RestoreShopFromSaveData"/> against the live <see cref="StorageFurniture"/>
    /// children of this shop. Idempotent — safe to call when no pending keys exist.
    ///
    /// <para>
    /// Call after the building's default-furniture spawn pass has finished (i.e. after
    /// <see cref="MWI.WorldSystem.BuildingSaveData.ComputeFurnitureKey"/> on every live
    /// <c>StorageFurniture</c> would yield a stable result). The MapController save/load
    /// flow runs this AFTER <c>RestoreStorageFurnitureContents</c> + <c>RestoreCashierContents</c>
    /// — at that point every authored storage on this building already exists.
    /// </para>
    ///
    /// <para>
    /// Per rule #31: per-key try/catch around the GetComponentsInChildren walk; one bad
    /// match never blocks the rest. Saved keys that don't resolve to any live storage are
    /// logged as warnings (the storage was renamed/removed since the save was written).
    /// </para>
    /// </summary>
    public void OnFurnituresLoaded()
    {
        if (!IsServer) return;
        if (_pendingSellShelfKeys == null) return;

        try
        {
            var allStorages = GetComponentsInChildren<StorageFurniture>(includeInactive: true);
            int resolved = 0;
            foreach (var savedKey in _pendingSellShelfKeys)
            {
                if (string.IsNullOrEmpty(savedKey)) continue;
                bool matched = false;
                for (int i = 0; i < allStorages.Length; i++)
                {
                    var storage = allStorages[i];
                    if (storage == null) continue;
                    string liveKey = MWI.WorldSystem.BuildingSaveData.ComputeFurnitureKey(storage, transform);
                    if (liveKey == savedKey)
                    {
                        // 2026-05-08 unified storage-role refactor: instead of stuffing the
                        // legacy _sellShelves list, write the role onto the storage's NetSync.
                        // Only writes if the role isn't already SellShelf (e.g. saved data may
                        // have ALSO populated StorageFurnitureSaveEntry.Role — first writer wins
                        // and the second is a no-op via NetworkVariable equality).
                        var sync = storage.GetComponent<StorageFurnitureNetworkSync>();
                        if (sync != null && storage.Role != StorageRoleType.SellShelf)
                        {
                            sync.SetRoleServer(StorageRoleType.SellShelf);
                            resolved++;
                        }
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    Debug.LogWarning($"<color=orange>[ShopBuilding:Restore]</color> {BuildingName}: saved sell-shelf key '{savedKey}' did not match any live StorageFurniture — entry dropped (storage likely renamed or removed since save).");
                }
            }

            // OnStorageRolesChanged fires per-write inside the role NetworkVariable's
            // OnValueChanged handler chain; no aggregate fire here needed.
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[ShopBuilding:Restore]</color> {BuildingName}: sell-shelf resolution failed.");
        }
        finally
        {
            // Always clear pending state so a second OnFurnituresLoaded call (idempotency)
            // is a true no-op even if the inner loop threw.
            _pendingSellShelfKeys = null;
        }
    }

    // ============================================================================
    // DEV-MODE OVERRIDES — host-only, gated by DevModeManager
    // ----------------------------------------------------------------------------
    // Each DevForce* peer mirrors the corresponding production ServerRpc minus the
    // ValidateOwnerCaller auth gate. They route through the same Do* helpers so
    // the production replication path (NetSync push + event invocation) is bit-for-bit
    // identical between owner-driven and dev-driven mutations.
    //
    // Authorisation: DevAssertHostAndDevMode (inherited from CommercialBuilding) checks
    // IsServer + DevModeManager.IsEnabled and emits the audit log line. Wrapped in
    // #if UNITY_EDITOR || DEVELOPMENT_BUILD so they are stripped from release builds.
    // ============================================================================

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Dev-only: <see cref="AddCatalogEntryServerRpc"/> minus owner auth.</summary>
    public void DevForceAddCatalogEntry(string itemId, int maxStock, int priceOverride)
    {
        if (!DevAssertHostAndDevMode("DevForceAddCatalogEntry")) return;
        DoAddCatalogEntry(itemId, maxStock, priceOverride);
    }

    /// <summary>Dev-only: <see cref="RemoveCatalogEntryServerRpc"/> minus owner auth.</summary>
    public void DevForceRemoveCatalogEntry(string itemId)
    {
        if (!DevAssertHostAndDevMode("DevForceRemoveCatalogEntry")) return;
        DoRemoveCatalogEntry(itemId);
    }

    /// <summary>Dev-only: <see cref="EditCatalogEntryServerRpc"/> minus owner auth.</summary>
    public void DevForceEditCatalogEntry(string itemId, int newMaxStock, int newPriceOverride)
    {
        if (!DevAssertHostAndDevMode("DevForceEditCatalogEntry")) return;
        DoEditCatalogEntry(itemId, newMaxStock, newPriceOverride);
    }

    // SellShelf assignment migrated to CommercialBuilding.TrySetStorageRoleServerRpc;
    // dev-mode override is CommercialBuilding.DevForceSetStorageRole (added 2026-05-09).

    /// <summary>
    /// Dev-only: <see cref="WithdrawCashierTillServerRpc"/> minus owner auth.
    /// Caller must specify an explicit recipient — the dev panel typically passes
    /// the host's local Character (the dev's own pawn).
    /// </summary>
    public void DevForceWithdrawCashierTill(Cashier cashier, Character recipient)
    {
        if (!DevAssertHostAndDevMode("DevForceWithdrawCashierTill")) return;
        if (cashier == null || recipient == null) return;
        var net = cashier.GetComponent<NetworkObject>();
        if (net == null) return;
        DoWithdrawCashierTill(new NetworkObjectReference(net), recipient);
    }
#endif
}
