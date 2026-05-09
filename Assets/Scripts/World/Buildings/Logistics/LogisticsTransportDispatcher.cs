using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns the transport-dispatch side of logistics for one
/// <see cref="CommercialBuilding"/>. Given an <see cref="LogisticsOrderBook"/>
/// of active client BuyOrders, it reserves the physical item instances,
/// creates a <see cref="TransportOrder"/> and queues it for a courier.
///
/// Collaborators:
/// - <see cref="LogisticsOrderBook"/> to read active orders and write placed transport orders.
/// - <see cref="BuildingManager"/> to find the nearest <see cref="TransporterBuilding"/>.
///
/// Intentionally knows nothing about stock policies — that's
/// <see cref="LogisticsStockEvaluator"/>'s job.
/// </summary>
public class LogisticsTransportDispatcher
{
    private readonly CommercialBuilding _building;
    private readonly LogisticsOrderBook _orderBook;
    private readonly BuildingLogisticsManager _facade; // diagnostic log toggle + PlaceCraftingOrder callbacks

    public LogisticsTransportDispatcher(CommercialBuilding building, LogisticsOrderBook orderBook, BuildingLogisticsManager facade)
    {
        _building = building;
        _orderBook = orderBook;
        _facade = facade;
    }

    /// <summary>
    /// Find any <see cref="TransporterBuilding"/> in the scene. Currently
    /// returns the first one — future work could rank by distance or
    /// workload. Kept stable so Layer C is byte-identical to A+B.
    /// </summary>
    public CommercialBuilding FindTransporterBuilding()
    {
        if (BuildingManager.Instance == null) return null;

        try
        {
            foreach (var b in BuildingManager.Instance.allBuildings)
            {
                if (b is TransporterBuilding trans) return trans;
            }
        }
        catch (System.Exception e)
        {
            // Defensive coding (rule #31): BuildingManager.allBuildings can be mutated on other threads;
            // an iteration hiccup must not bring down the whole logistics tick.
            Debug.LogException(e);
            Debug.LogError($"[LogisticsTransportDispatcher] {_building?.BuildingName}: FindTransporterBuilding threw while iterating BuildingManager.allBuildings. Returning null.");
        }
        return null;
    }

    /// <summary>
    /// Main per-tick entry point for the supplier-side of logistics. For every
    /// active BuyOrder, either dispatch a <see cref="TransportOrder"/> (if we
    /// have physical stock) or escalate to a <see cref="CraftingOrder"/>
    /// (if this is a crafting building and there's no craft yet in progress).
    ///
    /// Early-exits when <see cref="LogisticsOrderBook.IsDispatchDirty"/> is false —
    /// nothing has changed since the last pass, so re-running would do zero useful
    /// work and pay the full BuildGloballyReservedSet + LINQ cost. See
    /// wiki/projects/optimisation-backlog.md entry #2 / B for the perf rationale.
    /// </summary>
    public void ProcessActiveBuyOrders()
    {
        if (!_orderBook.IsDispatchDirty) return;

        var globallyReservedItems = _orderBook.BuildGloballyReservedSet();

        var activeOrders = _orderBook.ActiveOrdersForIteration();
        var activeCraftingOrders = _orderBook.ActiveCraftingOrdersForIteration();
        var placedTransportOrders = _orderBook.PlacedTransportOrdersForIteration();

        for (int i = activeOrders.Count - 1; i >= 0; i--)
        {
            var buyOrder = activeOrders[i];

            int remainingToDispatch = buyOrder.Quantity - buyOrder.DispatchedQuantity;
            if (remainingToDispatch <= 0) continue;

            // Phase-B: orders that have failed reachability too many times are parked
            // until TimeManager.OnNewDay expires them via DecreaseRemainingDays. Stops
            // the dispatcher from spinning on a destination that no courier can reach
            // (e.g. building sitting on an island of unbaked NavMesh).
            if (buyOrder.IsReachabilityStalled)
            {
                if (_facade.LogLogisticsFlow)
                {
                    Debug.Log($"<color=#ff8866>[LogisticsDBG]</color> ProcessActiveBuyOrders → skipping reachability-stalled BuyOrder {buyOrder.Quantity}x {buyOrder.ItemToTransport?.ItemName} for {buyOrder.Destination?.BuildingName} (unreachableCount={buyOrder.PathUnreachableCount}). Will expire naturally via DecreaseRemainingDays.");
                }
                continue;
            }

            // Already have a transport order queued for this destination+item → skip.
            if (placedTransportOrders.Any(t =>
                    t.ItemToTransport == buyOrder.ItemToTransport
                 && t.Destination == buyOrder.Destination
                 && !t.IsPlaced))
            {
                continue;
            }

            // V2 Logistics: let virtual buildings (e.g. HarvestingBuilding) inject physical instances lazily.
            _building.TryFulfillOrder(buyOrder, remainingToDispatch);

            var physicallyAvailableInstances = _building.Inventory
                .Where(inst => inst.ItemSO == buyOrder.ItemToTransport && !globallyReservedItems.Contains(inst))
                .ToList();

            if (physicallyAvailableInstances.Count >= remainingToDispatch)
            {
                DispatchTransportOrder(buyOrder, remainingToDispatch, physicallyAvailableInstances, globallyReservedItems);

                var linkedCompletedCraft = activeCraftingOrders.FirstOrDefault(c => c.IsCompleted && c.ItemToCraft == buyOrder.ItemToTransport);
                if (linkedCompletedCraft != null) _orderBook.RemoveActiveCraftingOrder(linkedCompletedCraft);
            }
            else
            {
                HandleInsufficientStock(buyOrder, remainingToDispatch, physicallyAvailableInstances, globallyReservedItems);
            }
        }

        // Clear the dirty flag so the next tick can early-exit if nothing changes.
        // Dispatches above DO mark the book dirty again (AddPlacedTransportOrder /
        // EnqueuePending), but those state changes have already been processed this
        // same call — clearing the flag here is correct. Any caller mutation that
        // happens AFTER this method returns will mark dirty afresh and trigger the
        // next pass.
        _orderBook.ClearDispatchDirty();
    }

    private void HandleInsufficientStock(BuyOrder buyOrder, int remainingToDispatch, List<ItemInstance> physicallyAvailableInstances, HashSet<ItemInstance> globallyReservedItems)
    {
        var activeCraftingOrders = _orderBook.ActiveCraftingOrdersForIteration();
        bool craftInProgress = activeCraftingOrders.Any(c => !c.IsCompleted && c.ItemToCraft == buyOrder.ItemToTransport);

        int actuallyAvailableStock = physicallyAvailableInstances.Count;
        int safeAvailable = Mathf.Max(0, actuallyAvailableStock);

        var stolenProvenOrder = activeCraftingOrders.FirstOrDefault(c => c.IsCompleted && c.ItemToCraft == buyOrder.ItemToTransport);

        if (stolenProvenOrder != null)
        {
            // Gate: during normal operation, items spawned at the CraftingStation's output
            // point need 1–N ticks before GatherStorageItems (or RefreshStorageInventory
            // Pass 2) moves them into _inventory. In that window physicallyAvailableInstances
            // is 0 even though the crafted items are physically on the ground inside the
            // building. Firing the "theft" branch here would replace the completed
            // CraftingOrder with a fresh one and make the blacksmith re-craft the whole
            // batch — producing 10 items for an order of 3. Only treat it as theft if the
            // items are truly gone from the BuildingZone, not merely unabsorbed.
            int unabsorbedInBuilding = _building.CountUnabsorbedItemsInBuildingZone(buyOrder.ItemToTransport);
            if (safeAvailable + unabsorbedInBuilding >= stolenProvenOrder.Quantity)
            {
                if (_facade.LogLogisticsFlow)
                {
                    Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> HandleInsufficientStock → completed craft for {buyOrder.ItemToTransport.ItemName} looks intact (inventory={safeAvailable} + unabsorbed={unabsorbedInBuilding} ≥ crafted={stolenProvenOrder.Quantity}). Skipping 'theft' branch; letting absorption catch up.");
                }
                return;
            }

            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color> 🚨 VOL DETECTÉ: Craft fini pour {buyOrder.ItemToTransport.ItemName} mais items manquants! Livraison partielle de {safeAvailable}.");

            if (safeAvailable > 0)
            {
                DispatchTransportOrder(buyOrder, safeAvailable, physicallyAvailableInstances, globallyReservedItems);
            }

            _orderBook.RemoveActiveCraftingOrder(stolenProvenOrder);

            int newRemainingToDispatch = buyOrder.Quantity - buyOrder.DispatchedQuantity;
            if (newRemainingToDispatch > 0)
            {
                var craftOrder = new CraftingOrder(
                    buyOrder.ItemToTransport,
                    newRemainingToDispatch,
                    buyOrder.RemainingDays,
                    _building.Owner,
                    buyOrder.Destination,
                    _building
                );
                _facade.PlaceCraftingOrder(craftOrder);
                Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   🔨 Nouvelle commande de craft interne ({newRemainingToDispatch}x) lancée pour compenser le vol.");
            }
        }
        else if (_building.RequiresCraftingFor(buyOrder.ItemToTransport) && !craftInProgress)
        {
            var craftOrder = new CraftingOrder(
                buyOrder.ItemToTransport,
                remainingToDispatch,
                buyOrder.RemainingDays,
                _building.Owner,
                buyOrder.Destination,
                _building
            );
            _facade.PlaceCraftingOrder(craftOrder);
            Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   🔨 Génération d'un ordre de craft interne pour la BuyOrder de {buyOrder.Destination.BuildingName}.");
        }
    }

    private void DispatchTransportOrder(BuyOrder buyOrder, int amountToDispatch, List<ItemInstance> availableInstances, HashSet<ItemInstance> globallyReservedItems)
    {
        if (_facade.LogLogisticsFlow)
        {
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> DispatchTransportOrder → source='{_building?.BuildingName}', dest='{buyOrder.Destination?.BuildingName}', item={buyOrder.ItemToTransport?.ItemName}, qty={amountToDispatch}, availablePhysical={availableInstances?.Count ?? 0}.");
        }

        var transporter = FindTransporterBuilding();
        if (transporter == null)
        {
            // Upgraded to Error in A+B: a missing TransporterBuilding silently stalls the whole pipeline.
            Debug.LogError($"<color=red>[BuildingLogisticsManager]</color> No TransporterBuilding in scene — cannot dispatch. source='{_building?.BuildingName}', dest='{buyOrder.Destination?.BuildingName}', item='{buyOrder.ItemToTransport?.ItemName}', qty={amountToDispatch}, buyOrderRemainingDays={buyOrder.RemainingDays}. This BuyOrder will stall until a TransporterBuilding is placed.");
            return;
        }

        var transportOrder = new TransportOrder(
            buyOrder.ItemToTransport,
            amountToDispatch,
            _building,
            buyOrder.Destination,
            buyOrder
        );

        for (int j = 0; j < amountToDispatch; j++)
        {
            var instanceToReserve = availableInstances[j];
            transportOrder.ReserveItem(instanceToReserve);
            buyOrder.ReserveItem(instanceToReserve);
            globallyReservedItems.Add(instanceToReserve);
        }

        buyOrder.RecordDispatch(amountToDispatch);

        _orderBook.AddPlacedTransportOrder(transportOrder);
        _orderBook.EnqueuePending(new BuildingLogisticsManager.PendingOrder(transportOrder, transporter));

        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   🚚 Expédition de {amountToDispatch}x {buyOrder.ItemToTransport.ItemName} vers {buyOrder.Destination.BuildingName} préparée avec réservation physique stricte.");

        if (_facade.LogLogisticsFlow)
        {
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> DispatchTransportOrder ← transporter='{transporter.BuildingName}', reserved={amountToDispatch} instance(s). TransportOrder queued.");
        }
    }

    /// <summary>
    /// Re-queue any un-placed BuyOrders and TransportOrders that failed to
    /// reach a recipient (e.g. courier was busy). Called once per tick by
    /// <see cref="JobLogisticsManager.Execute"/>.
    ///
    /// Early-exits when <see cref="LogisticsOrderBook.IsDispatchDirty"/> is false —
    /// retry is idempotent on a stable state, so re-running adds nothing. Shares
    /// the dirty flag with <see cref="ProcessActiveBuyOrders"/>; a state change
    /// triggers both methods together. The actual ClearDispatchDirty happens at
    /// the end of <see cref="ProcessActiveBuyOrders"/> (called immediately after
    /// us in <see cref="JobLogisticsManager.Execute"/>).
    /// </summary>
    public void RetryUnplacedOrders(Character worker)
    {
        if (!_orderBook.IsDispatchDirty) return;

        string workerName = worker != null ? worker.CharacterName : "LogisticsManager";

        foreach (var order in _orderBook.PlacedBuyOrdersForIteration())
        {
            if (!order.IsPlaced && !order.IsCompleted)
            {
                bool alreadyInQueue = _orderBook.PendingContains(p => p.Type == BuildingLogisticsManager.OrderType.Buy && p.BuyOrder == order);
                if (!alreadyInQueue)
                {
                    Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color> {workerName} : La BuyOrder de {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Source.BuildingName} avait échoué. On retente.");
                    _orderBook.EnqueuePending(new BuildingLogisticsManager.PendingOrder(order, order.Source));
                }
            }
        }

        foreach (var order in _orderBook.PlacedTransportOrdersForIteration())
        {
            if (!order.IsPlaced && !order.IsCompleted)
            {
                bool alreadyInQueue = _orderBook.PendingContains(p => p.Type == BuildingLogisticsManager.OrderType.Transport && p.TransportOrder == order);
                if (!alreadyInQueue)
                {
                    var transporter = FindTransporterBuilding();
                    if (transporter != null)
                    {
                        Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color> {workerName} : La TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} vers {order.Destination.BuildingName} avait échoué. On retente.");
                        _orderBook.EnqueuePending(new BuildingLogisticsManager.PendingOrder(order, transporter));
                    }
                }
            }
        }
    }
}
