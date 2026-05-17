using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Time;

/// <summary>
/// Thin MonoBehaviour facade for the logistics subsystem of one
/// <see cref="CommercialBuilding"/>. Layer C split the legacy monolithic
/// class into three single-purpose collaborators while keeping the public
/// API byte-identical so every external caller
/// (<see cref="JobLogisticsManager"/>, <see cref="InteractionPlaceOrder"/>,
/// <see cref="GoapAction_PlaceOrder"/>, <see cref="CommercialBuilding"/>,
/// editor tools, tests) continues to compile and behave the same way.
///
/// The three collaborators live in
/// <c>Assets/Scripts/World/Buildings/Logistics/</c>:
/// - <see cref="LogisticsOrderBook"/> — state (all order lists + pending queue).
/// - <see cref="LogisticsTransportDispatcher"/> — reserve items + queue transport.
/// - <see cref="LogisticsStockEvaluator"/> — policy-driven stock checks + supplier lookup.
///
/// This mirrors the Character facade pattern (<c>Character.cs</c>) at the
/// building scale: one dependency anchor exposing authorable fields, three
/// specialised services behind it.
/// </summary>
[RequireComponent(typeof(CommercialBuilding))]
public class BuildingLogisticsManager : MonoBehaviour
{
    // =========================================================================
    // AUTHORED FIELDS (Inspector)
    // =========================================================================

    [Header("Diagnostics")]
    [Tooltip("Toggle to emit verbose [LogisticsDBG] traces for this building's decision points " +
             "(stock checks, supplier lookup, dispatch outcomes). Per-building so designers can " +
             "isolate one forge or shop without drowning the console.")]
    [SerializeField] private bool _logLogisticsFlow = false;

    [Header("Stocking Policy")]
    [Tooltip("Per-building stocking strategy. If null, defaults to a MinStockPolicy loaded from " +
             "Resources/Data/Logistics/DefaultMinStockPolicy, falling back to a runtime instance " +
             "if no asset is present. Drop a ReorderPointPolicy or JustInTimePolicy asset here to " +
             "change this building's behaviour without touching code.")]
    [SerializeField] private LogisticsPolicy _logisticsPolicy = null;

    /// <summary>
    /// Exposes the diagnostics toggle to external job scripts (e.g. <see cref="JobLogisticsManager"/>)
    /// so the whole logistics flow for one building can be traced from a single Inspector field.
    /// </summary>
    public bool LogLogisticsFlow => _logLogisticsFlow;

    /// <summary>
    /// The resolved <see cref="ILogisticsPolicy"/> currently active on this building.
    /// Never null at runtime — falls back to a <see cref="MinStockPolicy"/>
    /// instance if the Inspector slot is empty.
    /// </summary>
    public ILogisticsPolicy Policy => _logisticsPolicy;

    // =========================================================================
    // NESTED TYPES (kept here for serialization compat with external callers)
    // =========================================================================

    public enum OrderType { Buy, Crafting, Transport }

    public struct PendingOrder
    {
        public OrderType Type;
        public BuyOrder BuyOrder;
        public CraftingOrder CraftingOrder;
        public TransportOrder TransportOrder;
        public CommercialBuilding TargetBuilding;

        public PendingOrder(BuyOrder order, CommercialBuilding target)
        {
            Type = OrderType.Buy;
            BuyOrder = order;
            CraftingOrder = null;
            TransportOrder = null;
            TargetBuilding = target;
        }

        public PendingOrder(CraftingOrder order, CommercialBuilding target)
        {
            Type = OrderType.Crafting;
            BuyOrder = null;
            CraftingOrder = order;
            TransportOrder = null;
            TargetBuilding = target;
        }

        public PendingOrder(TransportOrder order, CommercialBuilding target)
        {
            Type = OrderType.Transport;
            BuyOrder = null;
            CraftingOrder = null;
            TransportOrder = order;
            TargetBuilding = target;
        }
    }

    // =========================================================================
    // COLLABORATORS (plain C#, lazily constructed)
    // =========================================================================

    private CommercialBuilding _building;
    private LogisticsOrderBook _orderBook;
    private LogisticsTransportDispatcher _dispatcher;
    private LogisticsStockEvaluator _evaluator;

    // Public accessors so tests / advanced tooling can address a specific sub-component.
    public LogisticsOrderBook OrderBook => _orderBook;
    public LogisticsTransportDispatcher Dispatcher => _dispatcher;
    public LogisticsStockEvaluator Evaluator => _evaluator;

    // =========================================================================
    // PASS-THROUGH STATE READERS (public API compatibility — external callers)
    // =========================================================================

    public IReadOnlyList<BuyOrder> ActiveOrders => _orderBook.ActiveOrders;
    public IReadOnlyList<BuyOrder> PlacedBuyOrders => _orderBook.PlacedBuyOrders;
    public IReadOnlyList<TransportOrder> PlacedTransportOrders => _orderBook.PlacedTransportOrders;
    public IReadOnlyList<TransportOrder> ActiveTransportOrders => _orderBook.ActiveTransportOrders;
    public IReadOnlyList<CraftingOrder> ActiveCraftingOrders => _orderBook.ActiveCraftingOrders;
    public IReadOnlyList<BuildOrder> ActiveBuildOrders => _orderBook != null ? _orderBook.ActiveBuildOrders : System.Array.Empty<BuildOrder>();
    public bool HasPendingOrders => _orderBook.HasPendingOrders;

    /// <summary>Server-only facade. Adds a BuildOrder so JobBuilder employees can pick it up.</summary>
    public bool AddBuildOrder(BuildOrder order) => _orderBook != null && _orderBook.AddBuildOrder(order);

    /// <summary>Server-only facade. Removes a BuildOrder (on completion / cancellation).</summary>
    public bool RemoveBuildOrder(BuildOrder order) => _orderBook != null && _orderBook.RemoveBuildOrder(order);

    /// <summary>Server-only facade. JobBuilder uses this to pick the next BuildOrder to work.</summary>
    public BuildOrder GetFirstActiveBuildOrder() => _orderBook?.GetFirstActiveBuildOrder();

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        _building = GetComponent<CommercialBuilding>();

        EnsurePolicy();

        _orderBook = new LogisticsOrderBook();
        _dispatcher = new LogisticsTransportDispatcher(_building, _orderBook, this);
        _evaluator = new LogisticsStockEvaluator(_building, _orderBook, this);

        // Building-level restock-dirty hooks (subscribe once per lifetime; mirrored by OnDestroy).
        SubscribeRestockDirtyHooks();
    }

    private void Start()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += CheckExpiredOrders;
        }
    }

    private void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= CheckExpiredOrders;
        }

        // Unbind restock-dirty hooks (both building-level and per-storage) per rule #16.
        UnsubscribeRestockDirtyHooks();

        if (_orderBook == null) return;

        foreach (var order in _orderBook.ActiveOrders.ToList())
        {
            CancelBuyOrder(order);
        }
        foreach (var order in _orderBook.PlacedBuyOrders.ToList())
        {
            CancelBuyOrder(order);
        }
    }

    // =========================================================================
    // RESTOCK DIRTY HOOK WIRING
    // =========================================================================

    /// <summary>
    /// Per-storage subscription bookkeeping for <see cref="StorageFurniture.OnInventoryChanged"/>.
    /// Refreshed via <see cref="RefreshStorageContentSubscriptions"/> on every
    /// <see cref="CommercialBuilding.GetStorageFurnitureCached"/> rebuild — same chokepoint
    /// pattern used by the storage role-subscription fan-out, so newly-placed storages and
    /// removed storages stay in sync without per-callsite plumbing.
    /// </summary>
    private readonly List<StorageFurniture> _restockContentSubscribed = new List<StorageFurniture>();

    private void SubscribeRestockDirtyHooks()
    {
        if (_building == null) return;

        _building.OnStorageRolesChanged += HandleBuildingChangedForRestock;

        if (_building is ShopBuilding shop)
        {
            shop.OnCatalogChanged += HandleBuildingChangedForRestock;
        }
    }

    private void UnsubscribeRestockDirtyHooks()
    {
        if (_building != null)
        {
            _building.OnStorageRolesChanged -= HandleBuildingChangedForRestock;
            if (_building is ShopBuilding shop)
            {
                shop.OnCatalogChanged -= HandleBuildingChangedForRestock;
            }
        }

        // Drop any lingering per-storage subscriptions.
        for (int i = 0; i < _restockContentSubscribed.Count; i++)
        {
            var s = _restockContentSubscribed[i];
            if (s != null) s.OnInventoryChanged -= HandleStorageContentChangedForRestock;
        }
        _restockContentSubscribed.Clear();
    }

    /// <summary>
    /// Re-bind <see cref="StorageFurniture.OnInventoryChanged"/> subscriptions to the
    /// current set of building storages. Called from
    /// <see cref="CommercialBuilding.GetStorageFurnitureCached"/> right after the role
    /// subscription refresh — same chokepoint, same set. Idempotent; safe to call
    /// every rebuild. Allocation-free on the steady-state path (no add / no drop).
    /// </summary>
    internal void RefreshStorageContentSubscriptions(IReadOnlyList<StorageFurniture> currentSet)
    {
        // Drop subscriptions to storages no longer present (destroyed / removed).
        for (int i = _restockContentSubscribed.Count - 1; i >= 0; i--)
        {
            var s = _restockContentSubscribed[i];
            bool stillPresent = false;
            if (s != null && currentSet != null)
            {
                for (int j = 0; j < currentSet.Count; j++)
                {
                    if (currentSet[j] == s) { stillPresent = true; break; }
                }
            }
            if (!stillPresent)
            {
                if (s != null) s.OnInventoryChanged -= HandleStorageContentChangedForRestock;
                _restockContentSubscribed.RemoveAt(i);
            }
        }
        // Add subscriptions for newly-present storages.
        if (currentSet != null)
        {
            for (int i = 0; i < currentSet.Count; i++)
            {
                var s = currentSet[i];
                if (s == null) continue;
                bool already = false;
                for (int j = 0; j < _restockContentSubscribed.Count; j++)
                {
                    if (_restockContentSubscribed[j] == s) { already = true; break; }
                }
                if (already) continue;
                s.OnInventoryChanged += HandleStorageContentChangedForRestock;
                _restockContentSubscribed.Add(s);
            }
        }
    }

    // Single shared handler for all "building-level" triggers (catalog edits + role flips).
    // No allocations, no logging — pure flag write.
    private void HandleBuildingChangedForRestock() => _restockDirty = true;

    // Per-storage content-change handler. Same allocation-free pure flag write.
    private void HandleStorageContentChangedForRestock() => _restockDirty = true;

    /// <summary>
    /// Resolve <see cref="_logisticsPolicy"/> to a non-null value. Priority:
    /// (1) Inspector-assigned asset, (2) Resources/Data/Logistics/DefaultMinStockPolicy,
    /// (3) a runtime-created <see cref="MinStockPolicy"/> instance with a
    /// warning so designers notice they should author one.
    /// </summary>
    private void EnsurePolicy()
    {
        if (_logisticsPolicy != null) return;

        try
        {
            _logisticsPolicy = Resources.Load<LogisticsPolicy>("Data/Logistics/DefaultMinStockPolicy");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[BuildingLogisticsManager] {gameObject?.name}: Resources.Load for DefaultMinStockPolicy threw. Falling back to runtime MinStockPolicy.");
            _logisticsPolicy = null;
        }

        if (_logisticsPolicy == null)
        {
            Debug.LogWarning($"[BuildingLogisticsManager] {gameObject?.name}: no LogisticsPolicy asset assigned and no Resources/Data/Logistics/DefaultMinStockPolicy found — using a runtime MinStockPolicy instance. Behaviour is byte-identical to Layers A+B but designers should author a shared asset to avoid per-building allocations.");
            _logisticsPolicy = ScriptableObject.CreateInstance<MinStockPolicy>();
            _logisticsPolicy.name = "MinStockPolicy (runtime-fallback)";
        }
    }

    // =========================================================================
    // PUBLIC API — CANCELLATION
    // =========================================================================

    public void CancelBuyOrder(BuyOrder order)
    {
        if (order == null || _orderBook == null) return;

        bool wasRemoved = false;

        if (_orderBook.ActiveOrders.Contains(order))
        {
            _orderBook.RemoveActiveOrder(order);
            wasRemoved = true;
            Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> {_building?.BuildingName} : BuyOrder annulée/retirée (Fournisseur) : {order.Quantity}x {order.ItemToTransport.ItemName}");
        }

        if (_orderBook.PlacedBuyOrders.Contains(order))
        {
            _orderBook.RemovePlacedBuyOrder(order);
            _orderBook.FilterPending(p => p.BuyOrder != order);
            wasRemoved = true;
            Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> {_building?.BuildingName} : BuyOrder annulée/retirée (Client) : {order.Quantity}x {order.ItemToTransport.ItemName}");
        }

        // Remove any strictly linked TransportOrder that hasn't been physically sent yet
        var linkedTransports = _orderBook.PlacedTransportOrders.Where(t => t.AssociatedBuyOrder == order).ToList();
        foreach (var lt in linkedTransports)
        {
            _orderBook.RemovePlacedTransportOrder(lt);
            _orderBook.FilterPending(p => p.TransportOrder != lt);
            Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> {_building?.BuildingName} : TransportOrder liée annulée.");

            if (lt.Destination != null && lt.Destination.LogisticsManager != null)
            {
                lt.Destination.LogisticsManager.CancelActiveTransportOrder(lt);
            }
        }

        if (!wasRemoved) return;

        if (order.Source != null && order.Source != _building && order.Source.LogisticsManager != null)
        {
            order.Source.LogisticsManager.CancelBuyOrder(order);
        }

        if (order.Destination != null && order.Destination != _building && order.Destination.LogisticsManager != null)
        {
            order.Destination.LogisticsManager.CancelBuyOrder(order);
        }
    }

    public int GetReservedItemCount(ItemSO itemSO) => _orderBook?.GetReservedItemCount(itemSO) ?? 0;

    /// <summary>
    /// Pass-through to <see cref="LogisticsOrderBook.MarkDispatchDirty"/>. Called by
    /// <see cref="CommercialBuilding"/> on inventory mutations (AddToInventory,
    /// RemoveExactItemFromInventory, TakeFromInventory) so the dispatcher knows it
    /// needs to re-evaluate. Safe to call before <see cref="Awake"/> wires the
    /// order book — silently no-op until the book is constructed.
    /// </summary>
    public void MarkDispatchDirty()
    {
        if (_orderBook == null) return;
        _orderBook.MarkDispatchDirty();
    }

    // =========================================================================
    // RESTOCK DIRTY FLAG — server-only, gates GoapAction_RestockSellShelves
    // -------------------------------------------------------------------------
    // GoapAction_RestockSellShelves.IsValid runs at the BT/GOAP planner tick rate
    // for every worker employed at a ShopBuilding. On a stable inventory + stable
    // catalog its inner walk does the expensive
    //   _shop.GetItemsInStorageFurniture()  (slot enumeration across rooms)
    //   × FindShelfWithSpace(...)            (HasFreeSpaceForItem per SellShelf)
    // every tick and returns false ~100% of the time. This flag lets the planner
    // early-exit when nothing relevant changed since the last full scan.
    //
    // Initial value = true so the first IsValid after Awake / load-from-save
    // runs the full walk (covers warm-start, first punch-in, etc).
    //
    // Lifecycle:
    //  - Set:   inventory mutations on the owning CommercialBuilding (Add/Take/
    //           RemoveExact*), any StorageFurniture.OnInventoryChanged on this
    //           building's storages, the building-level OnStorageRolesChanged,
    //           ShopBuilding.OnCatalogChanged (catalog add/remove/edit), the
    //           tail of AssignStorageRolesForShift if any role actually changed,
    //           and the tail of a successful RestockSellShelves Execute that
    //           actually moved an item.
    //  - Clear: GoapAction_RestockSellShelves.IsValid after the full scan
    //           proves zero candidates exist. Called exactly once.
    //
    // Server-only: clients never run GOAP, so the flag is never read off-server.
    // Mirror the dispatcher dirty flag's pattern; deliberately a sibling, not a
    // replacement — distinct concerns warrant distinct flags.
    // =========================================================================

    private bool _restockDirty = true;

    /// <summary>True when something has changed that may invalidate the previous
    /// "nothing to restock" verdict. Read by <see cref="GoapAction_RestockSellShelves"/>'s
    /// IsValid as a single-field early-exit gate.</summary>
    public bool IsRestockDirty() => _restockDirty;

    /// <summary>Mark the restock scan as dirty so the next
    /// <see cref="GoapAction_RestockSellShelves"/> IsValid does the full walk.
    /// Idempotent — calling twice in a row is the same as once. No allocations.</summary>
    public void MarkRestockDirty() => _restockDirty = true;

    /// <summary>Called by <see cref="GoapAction_RestockSellShelves.IsValid"/>
    /// once a scan proves there is nothing left to restock. Subsequent IsValid
    /// calls short-circuit until something marks the flag dirty again.</summary>
    public void ClearRestockDirty() => _restockDirty = false;

    // =========================================================================
    // PUBLIC API — ORDER PLACEMENT (server-authoritative)
    // =========================================================================

    public bool PlaceBuyOrder(BuyOrder order)
    {
        RefreshStorageOnOrderReceived(nameof(PlaceBuyOrder));

        if (!_orderBook.AddActiveOrder(order)) return false;
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> Commande reçue : {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Destination.BuildingName}. Jours restants : {order.RemainingDays}");
        return true;
    }

    public bool PlaceCraftingOrder(CraftingOrder order)
    {
        RefreshStorageOnOrderReceived(nameof(PlaceCraftingOrder));

        if (!_orderBook.AddActiveCraftingOrder(order)) return false;
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> Commande Craft reçue : {order.Quantity}x {order.ItemToCraft.ItemName}. Jours restants : {order.RemainingDays}");

        if (_building is CraftingBuilding craftingBuilding)
        {
            _evaluator.CheckCraftingIngredients(craftingBuilding);
        }
        return true;
    }

    public bool PlaceTransportOrder(TransportOrder order)
    {
        if (!_orderBook.AddActiveTransportOrder(order)) return false;
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> Commande Transport reçue : {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Destination.BuildingName}.");
        return true;
    }

    /// <summary>
    /// Fresh audit of physical storage vs. logical inventory on every incoming BuyOrder /
    /// CraftingOrder. Without this, the dispatcher decides fulfillment (transport vs. craft)
    /// using stale stock data until the next <c>OnWorkerPunchIn</c> / <c>OnNewDay</c>.
    /// Not called for <see cref="PlaceTransportOrder"/>: that's received by a
    /// <c>TransporterBuilding</c> which has no physical stock of its own to reconcile.
    /// </summary>
    private void RefreshStorageOnOrderReceived(string callerTag)
    {
        if (_building == null) return;

        try
        {
            _building.RefreshStorageInventory();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[BuildingLogisticsManager] {_building?.BuildingName}: RefreshStorageInventory threw during {callerTag}. Continuing so the order still enters the book.");
        }
    }

    public TransportOrder GetNextAvailableTransportOrder() => _orderBook.GetNextAvailableTransportOrder();

    public CraftingOrder GetNextAvailableCraftingOrder() => _orderBook.GetNextAvailableCraftingOrder();

    // =========================================================================
    // PUBLIC API — PROGRESS / ACKNOWLEDGEMENT
    // =========================================================================

    public void UpdateOrderProgress(BuyOrder order, int deliveredAmount)
    {
        if (!_orderBook.ActiveOrders.Contains(order)) return;
        bool completed = order.RecordDelivery(deliveredAmount);
        if (completed)
        {
            _orderBook.RemoveActiveOrder(order);
            Debug.Log($"<color=green>[BuildingLogisticsManager]</color> Commande {order.Quantity}x {order.ItemToTransport.ItemName} COMPLÉTÉE.");
        }
    }

    public void UpdateTransportOrderProgress(TransportOrder order, int deliveredAmount)
    {
        if (!_orderBook.ActiveTransportOrders.Contains(order)) return;

        bool completed = order.RecordDelivery(deliveredAmount);

        if (order.AssociatedBuyOrder != null)
        {
            bool buyOrderCompletedByThisDelivery = order.AssociatedBuyOrder.RecordDelivery(deliveredAmount);

            // Supplier reputation bump (+1) on full BuyOrder completion. Authored 2026-05-16
            // as the positive half of the reputation system — see commercial-treasury.md.
            // Fires once per BuyOrder (the moment RecordDelivery flips IsCompleted to true).
            if (buyOrderCompletedByThisDelivery && order.AssociatedBuyOrder.Source != null)
            {
                order.AssociatedBuyOrder.Source.TryChangeReputation(+1, $"DeliveredOrder_{order.AssociatedBuyOrder.ItemToTransport?.ItemName}_x{order.AssociatedBuyOrder.Quantity}");
            }

            var clientLogistics = order.Destination?.LogisticsManager;
            if (clientLogistics != null)
            {
                clientLogistics.OnItemsDeliveredByTransporter(order.AssociatedBuyOrder, deliveredAmount);

                var supplierLogistics = order.AssociatedBuyOrder.Source?.LogisticsManager;
                if (supplierLogistics != null)
                {
                    supplierLogistics.AcknowledgeDeliveryProgress(order.AssociatedBuyOrder, order, deliveredAmount);
                }
            }
        }

        if (completed)
        {
            _orderBook.RemoveActiveTransportOrder(order);
            Debug.Log($"<color=green>[BuildingLogisticsManager]</color> Commande Transport {order.Quantity}x {order.ItemToTransport.ItemName} COMPLÉTÉE.");
        }
    }

    public void UpdateCraftingOrderProgress(CraftingOrder order, int craftedAmount)
    {
        if (!_orderBook.ActiveCraftingOrders.Contains(order)) return;
        bool completed = order.RecordCraft(craftedAmount);
        if (completed)
        {
            Debug.Log($"<color=green>[BuildingLogisticsManager]</color> Commande Craft {order.Quantity}x {order.ItemToCraft.ItemName} COMPLÉTÉE. Maintenue en mémoire temporairement.");
        }
    }

    public void OnItemHarvested(ItemSO itemSO)
    {
        Debug.Log($"<color=gray>[BuildingLogisticsManager]</color> L'item {itemSO.ItemName} a été physiquement déplacé de la zone de livraison vers le stockage.");
    }

    public void OnItemsDeliveredByTransporter(BuyOrder clientOrder, int amount)
    {
        var myOrder = _orderBook.PlacedBuyOrders.FirstOrDefault(o => o == clientOrder);
        if (myOrder != null && myOrder.IsCompleted)
        {
            _orderBook.RemovePlacedBuyOrder(myOrder);
            Debug.Log($"<color=green><h2>[ECONOMY]</h2></color> <color=yellow>CONGRATULATIONS !</color> La commande de {myOrder.Quantity}x {myOrder.ItemToTransport.ItemName} a été entièrement livrée à {myOrder.Destination.BuildingName} !");

            Character clientBoss = myOrder.ClientBoss;
            Character supplierBoss = myOrder.Source?.Owner;

            if (clientBoss != null && supplierBoss != null && clientBoss != supplierBoss)
            {
                if (clientBoss.CharacterRelation != null) clientBoss.CharacterRelation.UpdateRelation(supplierBoss, 5);
            }
        }
    }

    public void AcknowledgeDeliveryProgress(BuyOrder clientOrder, TransportOrder exactTransportOrder, int amount = 1)
    {
        var myOrder = _orderBook.ActiveOrders.FirstOrDefault(o => o == clientOrder);
        if (myOrder != null && myOrder.IsCompleted)
        {
            _orderBook.RemoveActiveOrder(myOrder);
            Debug.Log($"<color=green>[BuildingLogisticsManager]</color> 🤝 Le client a acquitté avoir reçu {myOrder.Quantity}x {myOrder.ItemToTransport.ItemName} !");

            Character clientBoss = myOrder.ClientBoss;
            Character supplierBoss = myOrder.Source?.Owner;

            if (supplierBoss != null && clientBoss != null && supplierBoss != clientBoss)
            {
                if (supplierBoss.CharacterRelation != null) supplierBoss.CharacterRelation.UpdateRelation(clientBoss, 5);
            }
        }

        if (exactTransportOrder != null && _orderBook.PlacedTransportOrders.Contains(exactTransportOrder) && exactTransportOrder.IsCompleted)
        {
            _orderBook.RemovePlacedTransportOrder(exactTransportOrder);
            Debug.Log($"<color=gray>[BuildingLogisticsManager]</color> TransportOrder {exactTransportOrder.Quantity}x {exactTransportOrder.ItemToTransport.ItemName} retirée du suivi fournisseur.");
        }
    }

    public void CancelActiveTransportOrder(TransportOrder order)
    {
        if (order == null || !_orderBook.ActiveTransportOrders.Contains(order)) return;

        _orderBook.RemoveActiveTransportOrder(order);
        Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} retirée définitivement de la file active (Échec).");

        if (order.Source != null && order.Source.LogisticsManager != null)
        {
            order.Source.LogisticsManager.ReportCancelledTransporter(order);
        }
    }

    public void ReportCancelledTransporter(TransportOrder order)
    {
        if (order == null || !_orderBook.PlacedTransportOrders.Contains(order)) return;

        Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color> 🚨 Le transporteur a annulé la livraison pour {order.ItemToTransport.ItemName}. On annule le lien physique pour forcer la reprise.");

        order.ReservedItems.Clear();

        if (order.AssociatedBuyOrder != null)
        {
            int amountToRecover = order.Quantity - order.DeliveredQuantity;
            order.AssociatedBuyOrder.CancelDispatch(amountToRecover);
        }

        _orderBook.RemovePlacedTransportOrder(order);
        _orderBook.FilterPending(p => p.TransportOrder != order);
    }

    public void ReportMissingReservedItem(TransportOrder order)
    {
        if (order == null || !_orderBook.PlacedTransportOrders.Contains(order)) return;

        Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color> 🚨 Le transporteur a signalé des items réservés manquants pour {order.Quantity}x {order.ItemToTransport.ItemName}. Annulation de la commande physique pour forcer le recalcul logistics.");

        order.ReservedItems.Clear();

        if (order.AssociatedBuyOrder != null)
        {
            int amountToRecover = order.Quantity - order.DeliveredQuantity;
            order.AssociatedBuyOrder.CancelDispatch(amountToRecover);
        }

        _orderBook.RemovePlacedTransportOrder(order);
        _orderBook.FilterPending(p => p.TransportOrder != order);
    }

    // =========================================================================
    // PUBLIC API — TICK ENTRY POINTS
    // =========================================================================

    public void OnWorkerPunchIn(Character worker)
    {
        try
        {
            _building.RefreshStorageInventory();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[BuildingLogisticsManager] {_building?.BuildingName}: RefreshStorageInventory threw during OnWorkerPunchIn(worker={worker?.CharacterName}). Continuing with stock checks.");
        }

        string workerName = worker != null ? worker.CharacterName : "?";
        bool isStockProvider = _building is IStockProvider;
        if (_logLogisticsFlow)
        {
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> OnWorkerPunchIn → building='{_building?.BuildingName}', worker='{workerName}', isStockProvider={isStockProvider}, isCrafting={_building is CraftingBuilding}, activeCraftingOrders={_orderBook.ActiveCraftingOrders.Count}");
        }

        // Stock-maintenance pass: unified for any IStockProvider (Shop, CraftingBuilding, …).
        if (_building is IStockProvider provider)
        {
            _evaluator.CheckStockTargets(provider, worker);
        }

        // Commission-fulfilment pass: only CraftingBuildings aggregate ingredient demand.
        if (_building is CraftingBuilding crafting)
        {
            _evaluator.CheckCraftingIngredients(crafting, worker);
        }
    }

    public void ProcessActiveBuyOrders() => _dispatcher.ProcessActiveBuyOrders();

    public void RetryUnplacedOrders(Character worker = null) => _dispatcher.RetryUnplacedOrders(worker);

    /// <summary>
    /// Shift-punch storage-role assignment pass. Called by
    /// <see cref="CommercialBuilding.WorkerStartingShift"/> every time a worker punches
    /// in. Walks every <see cref="StorageFurniture"/> in the building (deterministic
    /// order: MainRoom first, then SubRooms; FurnitureManager registration order
    /// within each room) and applies the unified role-priority rule:
    /// <list type="bullet">
    ///   <item>If the building requires tools (<see cref="CommercialBuilding.GetToolStockItems"/>
    ///         yields anything) → first storage = <see cref="StorageRoleType.ToolStorage"/>,
    ///         all the rest = <see cref="StorageRoleType.InventoryStorage"/>.</item>
    ///   <item>Else if the building is a <see cref="ShopBuilding"/> → first storage =
    ///         <see cref="StorageRoleType.SellShelf"/>, all the rest =
    ///         <see cref="StorageRoleType.InventoryStorage"/>.</item>
    ///   <item>Otherwise → all storages = <see cref="StorageRoleType.InventoryStorage"/>.</item>
    /// </list>
    /// Tool-storage priority overrides shelf priority: a shop that somehow has required
    /// tools still picks <see cref="StorageRoleType.ToolStorage"/> first, the shelf
    /// falls back to <see cref="StorageRoleType.InventoryStorage"/> like every other
    /// non-first storage.
    /// <para>
    /// **Idempotent.** Each storage's current <see cref="StorageFurniture.Role"/> is
    /// compared against the policy verdict and the write only happens on a mismatch.
    /// Re-running on the same building (second worker on the same shift, repeat
    /// punch-ins) converges to the same answer with zero replication traffic.
    /// </para>
    /// <para>
    /// **Server-authoritative.** Mutates state via the existing
    /// <see cref="StorageFurnitureNetworkSync.SetRoleServer"/> path which writes to a
    /// <see cref="Unity.Netcode.NetworkVariable{T}"/> — clients pick the new role up
    /// through the standard <c>OnValueChanged</c> callback that fans out to
    /// <see cref="CommercialBuilding.OnStorageRolesChanged"/>. Skips work on remote
    /// clients (defence-in-depth — <see cref="CommercialBuilding.WorkerStartingShift"/>
    /// already guards, but a logistics-layer safety net is cheap).
    /// </para>
    /// <para>
    /// Storages whose role does not appear in
    /// <see cref="CommercialBuilding.SupportedStorageRoles"/> for the desired verdict
    /// are skipped with a warning — covers misauthored subclasses (e.g. a base
    /// <see cref="CommercialBuilding"/> with no <see cref="StorageRoleType.SellShelf"/>
    /// support but the rule somehow chose it).
    /// </para>
    /// </summary>
    public void AssignStorageRolesForShift()
    {
        if (_building == null) return;

        // Server-only mutator. Solo / offline path keeps working because
        // NetworkManager.Singleton is null or !IsListening — same guard idiom as
        // CommercialBuilding.WorkerStartingShift.
        if (Unity.Netcode.NetworkManager.Singleton != null
            && Unity.Netcode.NetworkManager.Singleton.IsListening
            && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            return;
        }

        var storages = _building.GetStorageFurnitureOrdered();
        if (storages == null || storages.Count == 0) return;

        // Policy verdict: does the building require tools?
        bool requiresTools = false;
        var toolItems = _building.GetToolStockItems();
        if (toolItems != null)
        {
            foreach (var t in toolItems)
            {
                if (t != null) { requiresTools = true; break; }
            }
        }

        // First-storage role per the rule. Tool-priority overrides shelf-priority.
        bool isShop = _building is ShopBuilding;
        StorageRoleType firstRole;
        if (requiresTools)        firstRole = StorageRoleType.ToolStorage;
        else if (isShop)          firstRole = StorageRoleType.SellShelf;
        else                      firstRole = StorageRoleType.InventoryStorage;
        StorageRoleType restRole  = StorageRoleType.InventoryStorage;

        var supported = _building.SupportedStorageRoles;

        bool anyRoleWritten = false;

        for (int i = 0; i < storages.Count; i++)
        {
            var storage = storages[i];
            if (storage == null) continue;

            StorageRoleType desired = (i == 0) ? firstRole : restRole;

            // Subtype filter: skip if the desired role isn't supported by this building.
            // Covers misauthored subclasses (no SellShelf support but rule chose it, etc.).
            if (!IsRoleSupported(supported, desired))
            {
                if (NPCDebug.VerboseJobs)
                {
                    Debug.LogWarning($"[Logistics] {_building.BuildingName}: AssignStorageRolesForShift wants {desired} for storage[{i}] but it's not in SupportedStorageRoles — skipping.");
                }
                continue;
            }

            // Route through the canonical CommercialBuilding.TrySetStorageRoleServer
            // helper so this NPC path and the player UI path (TrySetStorageRoleServerRpc)
            // share IDENTICAL side-effects: subtype filter, idempotency guard, the
            // SetRoleServer write, and the OnValueChanged → OnStorageRolesChanged
            // fan-out that refreshes the management panel on every peer.
            //
            // Returns false on same-state (idempotent), on unsupported role (subtype
            // filter), or on missing StorageFurnitureNetworkSync sibling. All three
            // surface their own diagnostics inside the helper — no log here.
            StorageRoleType previousRole = storage.Role;
            bool wrote = _building.TrySetStorageRoleServer(storage, desired);

            if (wrote)
            {
                anyRoleWritten = true;
                if (NPCDebug.VerboseJobs)
                {
                    Debug.Log($"<color=#66ccff>[Logistics]</color> {_building.BuildingName}: storage[{i}] '{storage.name}' role {previousRole} → {desired} (requiresTools={requiresTools}, isShop={isShop}).");
                }
            }
        }

        // Each successful role flip already fires OnStorageRolesChanged → our
        // HandleBuildingChangedForRestock subscription marks the flag. But for the
        // first-punch-in case (initial dirty=true from Awake → cleared by an earlier
        // IsValid no-op walk, then shift-start writes new roles), make sure the next
        // tick re-evaluates against the freshly-assigned shelf roles.
        if (anyRoleWritten) MarkRestockDirty();
    }

    private static bool IsRoleSupported(System.Collections.Generic.IReadOnlyList<StorageRoleDescriptor> supported, StorageRoleType role)
    {
        if (supported == null) return false;
        for (int i = 0; i < supported.Count; i++)
        {
            if (supported[i].Type == role) return true;
        }
        return false;
    }

    /// <summary>
    /// Sibling of <see cref="AssignStorageRolesForShift"/> for the new
    /// <see cref="SafeFurniture"/> primitive (2026-05-09). Walks every safe child
    /// of the building; any safe whose <see cref="SafeFurniture.Role"/> is
    /// <see cref="SafeRoleType.None"/> is flipped to
    /// <see cref="SafeRoleType.Treasury"/>. Idempotent — safes already at Treasury
    /// are skipped.
    ///
    /// <para>
    /// Routes through <see cref="CommercialBuilding.TrySetSafeRoleServer"/> →
    /// <c>DoSetSafeRole</c> for convergence with the player UI path (2026-05-17 —
    /// Phase 1.7 Safes section). Both the player ServerRpc and this NPC pass share
    /// the same validation, replication, and event fan-out — any future side-effect
    /// (cache invalidation, audit log, broadcast) added to <c>DoSetSafeRole</c>
    /// reaches both code paths automatically.
    /// </para>
    ///
    /// <para>
    /// Called from <see cref="CommercialBuilding.WorkerStartingShift"/> after the
    /// existing <see cref="AssignStorageRolesForShift"/> pass. Server-only
    /// (defence-in-depth — the caller already gates).
    /// </para>
    /// </summary>
    public void AssignSafeRolesForShift()
    {
        if (_building == null) return;

        if (Unity.Netcode.NetworkManager.Singleton != null
            && Unity.Netcode.NetworkManager.Singleton.IsListening
            && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            return;
        }

        var safes = _building.Safes;
        if (safes == null || safes.Count == 0) return;

        for (int i = 0; i < safes.Count; i++)
        {
            var safe = safes[i];
            if (safe == null) continue;
            if (safe.Role != SafeRoleType.None) continue;

            bool flipped = _building.TrySetSafeRoleServer(safe, SafeRoleType.Treasury);
            if (flipped && NPCDebug.VerboseJobs)
            {
                Debug.Log($"<color=#66ccff>[Logistics]</color> {_building.BuildingName}: safe[{i}] '{safe.name}' role None → Treasury (auto-assign on shift-punch).");
            }
        }
        // OnTreasuryChanged fires per safe via the per-safe NetworkVariable fan-out
        // (CommercialBuilding.HandleSafeRoleChanged); no extra notification needed.
    }

    // =========================================================================
    // PUBLIC API — PENDING QUEUE ACCESS (used by GoapAction_PlaceOrder)
    // =========================================================================

    public PendingOrder PeekPendingOrder() => _orderBook.PeekPending();

    public void DequeuePendingOrder() => _orderBook.DequeuePending();

    public void EnqueuePendingOrder(PendingOrder order) => _orderBook.EnqueuePending(order);

    // =========================================================================
    // INTERNAL — EXPIRATION SWEEP (subscribed on TimeManager.OnNewDay)
    // =========================================================================

    private void CheckExpiredOrders()
    {
        if (TimeManager.Instance == null) return;
        CheckExpiredBuyOrders();
        CheckExpiredCraftingOrders();
    }

    private void CheckExpiredBuyOrders()
    {
        if (_orderBook.ActiveOrders.Count > 0)
        {
            var expiredSupplierOrders = new List<BuyOrder>();
            foreach (var order in _orderBook.ActiveOrders)
            {
                order.DecreaseRemainingDays();
                if (order.RemainingDays <= 0) expiredSupplierOrders.Add(order);
            }

            foreach (var expired in expiredSupplierOrders)
            {
                Debug.Log($"<color=red>[BuildingLogisticsManager]</color> Commande Client {expired.Quantity}x {expired.ItemToTransport.ItemName} EXPIRÉE chez le fournisseur.");

                if (_building != null && _building.Owner != null)
                {
                    Character workplaceBoss = _building.Owner;
                    if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                    {
                        expired.ClientBoss.CharacterRelation?.UpdateRelation(workplaceBoss, -25);
                    }
                }

                int undelivered = expired.Quantity - expired.DeliveredQuantity;

                // B2B refund-on-expiration (2026-05-16). Only fires when:
                //   - the order's Source is this building (which is a ShopBuilding),
                //   - IsPlaced=true (the atomic commit happened — money + items moved),
                //   - undelivered > 0 (the transporter hadn't completed the run).
                // For non-B2B (producer) orders, money was never debited from the buyer's
                // treasury, so there's nothing to refund — only the reputation hit applies.
                if (undelivered > 0 && expired.IsPlaced && _building is ShopBuilding sourceShop && expired.Source == _building)
                {
                    TryRefundB2BExpiration(sourceShop, expired, undelivered);
                }

                // Reputation hit on the SUPPLIER (this building) for any undelivered units.
                // Same penalty applies to B2B and producer-side orders — both promised
                // delivery and failed. The buyer is unaffected; they did their part.
                if (undelivered > 0 && _building is CommercialBuilding supplierAsCommercial)
                {
                    supplierAsCommercial.TryChangeReputation(-2, $"ExpiredOrder_{expired.ItemToTransport?.ItemName}_undelivered{undelivered}");
                }

                // Transporter Building reputation hit (2026-05-17, Phase C3). Walks the
                // supplier's PlacedTransportOrders for any uncompleted TO whose
                // AssociatedBuyOrder == this expired order, then docks the HostTransporter
                // -5 per still-uncompleted TO. Multiple TOs from the same carrier compound
                // (multiple failed runs = larger hit, which is fair). MUST fire BEFORE
                // CancelBuyOrder below — CancelBuyOrder removes the associated TOs and
                // the HostTransporter linkage would be lost.
                if (undelivered > 0)
                {
                    var placedTOs = _orderBook.PlacedTransportOrders;
                    for (int i = 0; i < placedTOs.Count; i++)
                    {
                        var to = placedTOs[i];
                        if (to == null) continue;
                        if (to.AssociatedBuyOrder != expired) continue;
                        if (to.IsCompleted) continue;
                        if (to.HostTransporter == null) continue;
                        to.HostTransporter.TryChangeReputation(-2,
                            $"FailedTransport_{expired.ItemToTransport?.ItemName}_for_{expired.Destination?.BuildingName}");
                    }
                }

                CancelBuyOrder(expired);
            }
        }

        if (_orderBook.PlacedBuyOrders.Count > 0)
        {
            var expiredClientOrders = new List<BuyOrder>();
            foreach (var order in _orderBook.PlacedBuyOrders)
            {
                if (!order.IsPlaced) order.DecreaseRemainingDays();
                if (order.RemainingDays <= 0) expiredClientOrders.Add(order);
            }

            foreach (var expired in expiredClientOrders)
            {
                Debug.Log($"<color=red>[BuildingLogisticsManager]</color> Notre commande de {expired.Quantity}x {expired.ItemToTransport.ItemName} a EXPIRÉ et est retirée de nos suivis client.");
                CancelBuyOrder(expired);
            }
        }
    }

    /// <summary>
    /// Refund-on-expiration for a B2B BuyOrder (Source is a ShopBuilding, IsPlaced=true,
    /// some quantity undelivered). Authored 2026-05-16 — see
    /// <c>wiki/systems/commercial-treasury.md §Refund-on-expiration</c>.
    ///
    /// <para>
    /// Atomically reverses the financial half of the original B2B commit for the undelivered
    /// portion: debits the shop's first <see cref="Cashier"/> till, credits the buyer's
    /// <see cref="CommercialBuilding"/> treasury. If the cashier till has been emptied by the
    /// shop owner in the meantime, falls back to the shop's own treasury safe; if both are
    /// empty, refunds only what's available and logs a warning (the buyer still gets the rep
    /// hit on the supplier as the audit trail). Money never gets printed.
    /// </para>
    ///
    /// <para>
    /// Items that were moved from sell-shelves into shop.Inventory at commit time but never
    /// picked up by the transporter (still physically at the shop, never left in a courier's
    /// bag) are returned to a sell-shelf slot where possible, or left in shop.Inventory if no
    /// shelf slot is free. Items already in transit when the order expired are lost — the
    /// shop can't refund what isn't there. The financial refund + the lost items together
    /// nets the buyer back to their pre-order state for the undelivered units.
    /// </para>
    /// </summary>
    private void TryRefundB2BExpiration(ShopBuilding shop, BuyOrder expired, int undelivered)
    {
        if (shop == null || expired == null || undelivered <= 0) return;
        var buyer = expired.Destination;
        if (buyer == null) return;

        // Re-resolve unit price from the live catalog (the catalog may have changed since
        // commit, but using the live price keeps refund-amount predictable for the player —
        // and the catalog rarely shifts mid-order in practice).
        var catalogEntry = shop.GetCatalogEntry(expired.ItemToTransport);
        if (!catalogEntry.HasValue)
        {
            Debug.LogWarning($"[Refund] {shop.BuildingName}: expired B2B order for {expired.ItemToTransport?.ItemName} but item is no longer in catalog. Skipping refund — financial state may drift.");
            return;
        }
        int unitPrice = ShopBuilding.ResolvePrice(catalogEntry.Value);
        int refundOwed = unitPrice * undelivered;
        if (refundOwed <= 0) return;

        var currency = MWI.Economy.CurrencyId.Default;
        int actuallyRefunded = 0;

        // Tier 1: shop's first cashier till. Cashier already exposes DebitTill which returns
        // false on insufficient funds — partial-cover not supported at till level, so try the
        // full amount and fall through to treasury on failure.
        if (shop.Cashiers != null && shop.Cashiers.Count > 0)
        {
            var cashier = shop.Cashiers[0];
            if (cashier != null && cashier.DebitTill(currency, refundOwed, $"B2B_RefundExpired_to_{buyer.BuildingName}"))
            {
                actuallyRefunded = refundOwed;
            }
        }

        // Tier 2: shop's own treasury (if till didn't cover OR no cashier exists).
        if (actuallyRefunded < refundOwed)
        {
            int stillOwed = refundOwed - actuallyRefunded;
            if (shop.TryDebitTreasury(currency, stillOwed, $"B2B_RefundExpired_to_{buyer.BuildingName}"))
            {
                actuallyRefunded += stillOwed;
            }
        }

        if (actuallyRefunded > 0)
        {
            buyer.CreditTreasury(currency, actuallyRefunded, $"B2B_RefundExpired_from_{shop.BuildingName}");
        }
        if (actuallyRefunded < refundOwed)
        {
            Debug.LogWarning($"[Refund] {shop.BuildingName} → {buyer.BuildingName}: owed {refundOwed}g for {undelivered}× {expired.ItemToTransport.ItemName}, but till+treasury only covered {actuallyRefunded}g. Shortfall of {refundOwed - actuallyRefunded}g eaten by the buyer.");
        }
        else
        {
            Debug.Log($"<color=#aaffaa>[Refund]</color> {shop.BuildingName} → {buyer.BuildingName}: refunded {actuallyRefunded}g ({undelivered}× {expired.ItemToTransport.ItemName} undelivered).");
        }

        // Item return: any ItemInstance still in shop.Inventory that belongs to this expired
        // order's expected items goes back to a sell-shelf slot, best-effort. We can't tell
        // for certain "this specific instance was part of this order" without per-order
        // tagging — so we walk shop.Inventory for matching ItemSO and return up to
        // 'undelivered' units (the items still in transit are gone and the shop's books
        // are now in sync).
        int itemsReturned = ReturnExpiredOrderItemsToShelf(shop, expired.ItemToTransport, undelivered);
        if (itemsReturned > 0)
        {
            Debug.Log($"<color=#aaffaa>[Refund]</color> {shop.BuildingName}: returned {itemsReturned}× {expired.ItemToTransport.ItemName} from inventory back to sell-shelf (out of {undelivered} undelivered).");
        }
    }

    /// <summary>
    /// Best-effort return of matching ItemSO instances from shop.Inventory back onto a
    /// sell-shelf. Stops at <paramref name="maxCount"/> or when shelves saturate. Items
    /// that don't fit stay in shop.Inventory — they're still owned by the shop, just not
    /// visible to customers.
    /// </summary>
    private static int ReturnExpiredOrderItemsToShelf(ShopBuilding shop, ItemSO target, int maxCount)
    {
        if (shop == null || target == null || maxCount <= 0) return 0;
        var shelves = shop.SellShelves;
        if (shelves == null || shelves.Count == 0) return 0;

        // Walk inventory backwards so removal during iteration is safe.
        int returned = 0;
        var inventory = shop.Inventory;
        for (int i = inventory.Count - 1; i >= 0 && returned < maxCount; i--)
        {
            var inst = inventory[i];
            if (inst == null || inst.ItemSO != target) continue;

            // Find a shelf with free space for this instance.
            for (int s = 0; s < shelves.Count; s++)
            {
                var shelf = shelves[s];
                if (shelf == null || shelf.IsLocked) continue;
                if (!shelf.HasFreeSpaceForItem(inst)) continue;
                // Pull from inventory then store on shelf. RemoveExactItemFromInventory
                // is the symmetric mutator on CommercialBuilding.
                if (shop.RemoveExactItemFromInventory(inst))
                {
                    if (shelf.AddItem(inst))
                    {
                        returned++;
                        break;
                    }
                    // Failed to add to shelf after removing from inventory — put it back.
                    shop.AddToInventory(inst);
                }
                break;
            }
        }
        return returned;
    }

    private void CheckExpiredCraftingOrders()
    {
        if (_orderBook.ActiveCraftingOrders.Count == 0) return;

        var expiredOrders = new List<CraftingOrder>();

        foreach (var order in _orderBook.ActiveCraftingOrders)
        {
            if (order.IsCompleted) continue;

            order.DecreaseRemainingDays();
            if (order.RemainingDays <= 0) expiredOrders.Add(order);
        }

        foreach (var expired in expiredOrders)
        {
            _orderBook.RemoveActiveCraftingOrder(expired);
            Debug.Log($"<color=red>[BuildingLogisticsManager]</color> Commande Craft {expired.Quantity}x {expired.ItemToCraft.ItemName} EXPIRÉE.");

            if (_building != null && _building.Owner != null)
            {
                Character workplaceBoss = _building.Owner;
                if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                {
                    expired.ClientBoss.CharacterRelation?.UpdateRelation(workplaceBoss, -25);
                }
            }
        }
    }
}
