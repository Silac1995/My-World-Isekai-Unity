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
    public bool HasPendingOrders => _orderBook.HasPendingOrders;

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
            order.AssociatedBuyOrder.RecordDelivery(deliveredAmount);

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
