using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Pure state-keeping component owned by <see cref="BuildingLogisticsManager"/>.
/// Holds every list of orders a commercial building tracks and exposes
/// mutation + read APIs. Has no knowledge of transporters, policies, or
/// the scene graph — it is strictly a bookkeeping layer.
///
/// SRP split motivation (Layer C): the legacy
/// <c>BuildingLogisticsManager</c> mixed three responsibilities — state,
/// dispatch, and evaluation. Splitting makes each layer unit-testable in
/// isolation and prevents accidental coupling (e.g. the stock evaluator
/// grabbing a transporter reference it shouldn't know about).
/// </summary>
public class LogisticsOrderBook
{
    // --- Seller side: BuyOrders placed by external clients on this building ---
    private readonly List<BuyOrder> _activeOrders = new List<BuyOrder>();
    public IReadOnlyList<BuyOrder> ActiveOrders => _activeOrders;

    // --- Client side: BuyOrders this building has placed on suppliers ---
    private readonly List<BuyOrder> _placedBuyOrders = new List<BuyOrder>();
    public IReadOnlyList<BuyOrder> PlacedBuyOrders => _placedBuyOrders;

    // --- TransportOrders this building has created to fulfil its clients ---
    private readonly List<TransportOrder> _placedTransportOrders = new List<TransportOrder>();
    public IReadOnlyList<TransportOrder> PlacedTransportOrders => _placedTransportOrders;

    // --- TransportOrders accepted as destination (for TransporterBuilding etc.) ---
    private readonly List<TransportOrder> _activeTransportOrders = new List<TransportOrder>();
    public IReadOnlyList<TransportOrder> ActiveTransportOrders => _activeTransportOrders;

    // --- CraftingOrders commissioned to this building ---
    private readonly List<CraftingOrder> _activeCraftingOrders = new List<CraftingOrder>();
    public IReadOnlyList<CraftingOrder> ActiveCraftingOrders => _activeCraftingOrders;

    // --- Physical-walk queue (a courier must visit the supplier to 'place' each one) ---
    private Queue<BuildingLogisticsManager.PendingOrder> _pendingOrders = new Queue<BuildingLogisticsManager.PendingOrder>();
    public bool HasPendingOrders => _pendingOrders.Count > 0;
    public int PendingOrderCount => _pendingOrders.Count;

    // --- Quest-system publish events (Task 15). Surfaced by BuildingLogisticsManager / CommercialBuilding ---
    // Each event fires AFTER the order has been added to or removed from its list.
    public event System.Action<BuyOrder> OnBuyOrderAdded;
    public event System.Action<TransportOrder> OnTransportOrderAdded;
    public event System.Action<CraftingOrder> OnCraftingOrderAdded;
    public event System.Action<MWI.Quests.IQuest> OnAnyOrderRemoved;

    // --- Dispatcher dirty flag (perf, see wiki/projects/optimisation-backlog.md entry #2 / B).
    // LogisticsTransportDispatcher.ProcessActiveBuyOrders and RetryUnplacedOrders run at 10 Hz
    // per logistics manager (40 calls/sec across the audited 4-manager mix). On a stable order
    // book + stable inventory, those calls do zero useful work but pay the full
    // BuildGloballyReservedSet + per-order LINQ cost every time. The dirty flag lets the
    // dispatcher early-exit when nothing changed since the last pass.
    //
    // Sources of dirty:
    //  - Every Add*/Remove* method on this book.
    //  - Inventory mutations on the owning CommercialBuilding (AddToInventory, RemoveExact*,
    //    TakeFromInventory). Building hooks call MarkDispatchDirty on its order book.
    //  - Reachability state changes on a BuyOrder (rare — currently expires via OnNewDay
    //    so the dirty flag flips only when the order itself flips IsPlaced or RemainingDays).
    //
    // Initial state: dirty=true so the first dispatcher tick after Awake processes once
    // (covers warm-start cases like load from save where orders pre-exist).
    private bool _dispatchDirty = true;
    public bool IsDispatchDirty => _dispatchDirty;
    /// <summary>Mark the dispatcher as dirty so the next ProcessActiveBuyOrders / RetryUnplacedOrders runs.</summary>
    public void MarkDispatchDirty() => _dispatchDirty = true;
    /// <summary>Called by the dispatcher at the end of a successful pass.</summary>
    public void ClearDispatchDirty() => _dispatchDirty = false;

    // =========================================================================
    // ACTIVE ORDERS (supplier-side)
    // =========================================================================

    public bool AddActiveOrder(BuyOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;
        _activeOrders.Add(order);
        _dispatchDirty = true;
        return true;
    }

    public bool RemoveActiveOrder(BuyOrder order)
    {
        if (order == null) return false;
        bool removed = _activeOrders.Remove(order);
        if (removed) _dispatchDirty = true;
        return removed;
    }

    // =========================================================================
    // PLACED BUY ORDERS (client-side)
    // =========================================================================

    public void AddPlacedBuyOrder(BuyOrder order)
    {
        if (order == null) return;
        _placedBuyOrders.Add(order);
        _dispatchDirty = true;
        OnBuyOrderAdded?.Invoke(order);
    }

    public bool RemovePlacedBuyOrder(BuyOrder order)
    {
        if (order == null) return false;
        bool removed = _placedBuyOrders.Remove(order);
        if (removed)
        {
            _dispatchDirty = true;
            OnAnyOrderRemoved?.Invoke(order);
        }
        return removed;
    }

    public BuyOrder FindUnplacedBuyOrder(ItemSO item, CommercialBuilding supplier)
    {
        if (item == null || supplier == null) return null;
        return _placedBuyOrders.FirstOrDefault(o => o.ItemToTransport == item && o.Source == supplier && !o.IsPlaced);
    }

    public int SumInFlightQuantityFor(ItemSO item)
    {
        if (item == null) return 0;
        return _placedBuyOrders
            .Where(o => o.ItemToTransport == item && !o.IsCompleted)
            .Sum(o => o.Quantity);
    }

    // =========================================================================
    // TRANSPORT ORDERS
    // =========================================================================

    public void AddPlacedTransportOrder(TransportOrder order)
    {
        if (order == null) return;
        _placedTransportOrders.Add(order);
        _dispatchDirty = true;
        OnTransportOrderAdded?.Invoke(order);
    }

    public bool RemovePlacedTransportOrder(TransportOrder order)
    {
        if (order == null) return false;
        bool removed = _placedTransportOrders.Remove(order);
        if (removed)
        {
            _dispatchDirty = true;
            OnAnyOrderRemoved?.Invoke(order);
        }
        return removed;
    }

    public bool AddActiveTransportOrder(TransportOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;
        _activeTransportOrders.Add(order);
        _dispatchDirty = true;
        return true;
    }

    public bool RemoveActiveTransportOrder(TransportOrder order)
    {
        if (order == null) return false;
        bool removed = _activeTransportOrders.Remove(order);
        if (removed) _dispatchDirty = true;
        return removed;
    }

    public TransportOrder GetNextAvailableTransportOrder()
    {
        if (_activeTransportOrders.Count == 0) return null;
        foreach (var order in _activeTransportOrders)
        {
            if (order.Quantity > order.DeliveredQuantity + order.InTransitQuantity)
                return order;
        }
        return null;
    }

    // =========================================================================
    // CRAFTING ORDERS
    // =========================================================================

    public bool AddActiveCraftingOrder(CraftingOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;
        _activeCraftingOrders.Add(order);
        _dispatchDirty = true;
        OnCraftingOrderAdded?.Invoke(order);
        return true;
    }

    public bool RemoveActiveCraftingOrder(CraftingOrder order)
    {
        if (order == null) return false;
        bool removed = _activeCraftingOrders.Remove(order);
        if (removed)
        {
            _dispatchDirty = true;
            OnAnyOrderRemoved?.Invoke(order);
        }
        return removed;
    }

    public CraftingOrder GetNextAvailableCraftingOrder()
    {
        var pendingOrders = _activeCraftingOrders.Where(o => !o.IsCompleted).ToList();
        if (pendingOrders.Count == 0) return null;
        var nextOrder = pendingOrders[0];
        foreach (var order in pendingOrders)
        {
            if (order.RemainingDays < nextOrder.RemainingDays) nextOrder = order;
        }
        return nextOrder;
    }

    // =========================================================================
    // PENDING (courier) QUEUE
    // =========================================================================

    public BuildingLogisticsManager.PendingOrder PeekPending() => _pendingOrders.Peek();

    public void EnqueuePending(BuildingLogisticsManager.PendingOrder order)
    {
        _pendingOrders.Enqueue(order);
        _dispatchDirty = true;
    }

    public void DequeuePending()
    {
        if (_pendingOrders.Count > 0)
        {
            _pendingOrders.Dequeue();
            _dispatchDirty = true;
        }
    }

    /// <summary>
    /// Rebuild the pending queue keeping only entries that match the predicate.
    /// Used when cancelling an order — we must scrub every reference to it
    /// from the queue without disturbing the ordering of the remaining items.
    /// </summary>
    public void FilterPending(System.Func<BuildingLogisticsManager.PendingOrder, bool> keepPredicate)
    {
        if (keepPredicate == null) return;
        _pendingOrders = new Queue<BuildingLogisticsManager.PendingOrder>(_pendingOrders.Where(keepPredicate));
        _dispatchDirty = true;
    }

    public bool PendingContains(System.Func<BuildingLogisticsManager.PendingOrder, bool> predicate)
    {
        if (predicate == null) return false;
        return _pendingOrders.Any(predicate);
    }

    // =========================================================================
    // AGGREGATE QUERIES
    // =========================================================================

    /// <summary>
    /// Count distinct <see cref="ItemInstance"/>s reserved across every kind
    /// of order this building tracks. Callers use this to avoid double-reserving
    /// the same physical item instance.
    /// </summary>
    public int GetReservedItemCount(ItemSO itemSO)
    {
        if (itemSO == null) return 0;

        var reservedInstances = new HashSet<ItemInstance>();

        foreach (var tOrder in _placedTransportOrders)
            foreach (var item in tOrder.ReservedItems)
                if (item.ItemSO == itemSO) reservedInstances.Add(item);

        foreach (var bOrder in _placedBuyOrders)
            foreach (var item in bOrder.ReservedItems)
                if (item.ItemSO == itemSO) reservedInstances.Add(item);

        foreach (var aOrder in _activeOrders)
            foreach (var item in aOrder.ReservedItems)
                if (item.ItemSO == itemSO) reservedInstances.Add(item);

        return reservedInstances.Count;
    }

    /// <summary>
    /// Build the globally-reserved set over <see cref="PlacedTransportOrders"/>
    /// and <see cref="PlacedBuyOrders"/>. Used by the dispatcher to avoid
    /// double-reserving a single physical <see cref="ItemInstance"/>.
    /// </summary>
    public HashSet<ItemInstance> BuildGloballyReservedSet()
    {
        var set = new HashSet<ItemInstance>();

        foreach (var tOrder in _placedTransportOrders)
            foreach (var item in tOrder.ReservedItems)
                set.Add(item);

        foreach (var bOrder in _placedBuyOrders)
            foreach (var item in bOrder.ReservedItems)
                set.Add(item);

        return set;
    }

    // --- Direct writable list access for internal iteration (mutation safety: callers iterate copies) ---
    public List<BuyOrder> ActiveOrdersForIteration() => _activeOrders;
    public List<BuyOrder> PlacedBuyOrdersForIteration() => _placedBuyOrders;
    public List<TransportOrder> PlacedTransportOrdersForIteration() => _placedTransportOrders;
    public List<TransportOrder> ActiveTransportOrdersForIteration() => _activeTransportOrders;
    public List<CraftingOrder> ActiveCraftingOrdersForIteration() => _activeCraftingOrders;
}
