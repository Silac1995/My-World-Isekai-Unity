using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Stock-check and supplier-routing brain for one
/// <see cref="CommercialBuilding"/>. Given an <see cref="IStockProvider"/>,
/// compares virtual stock against each <see cref="StockTarget"/> via the
/// plugged-in <see cref="ILogisticsPolicy"/> and requests restocks.
///
/// Collaborators:
/// - <see cref="LogisticsOrderBook"/> for in-flight counts and new BuyOrder registration.
/// - <see cref="ILogisticsPolicy"/> for the "how much to order" decision.
/// - <see cref="BuildingManager"/> for supplier discovery.
///
/// Does no transport work — that's <see cref="LogisticsTransportDispatcher"/>.
/// </summary>
public class LogisticsStockEvaluator
{
    private readonly CommercialBuilding _building;
    private readonly LogisticsOrderBook _orderBook;
    private readonly BuildingLogisticsManager _facade;

    public LogisticsStockEvaluator(CommercialBuilding building, LogisticsOrderBook orderBook, BuildingLogisticsManager facade)
    {
        _building = building;
        _orderBook = orderBook;
        _facade = facade;
    }

    /// <summary>
    /// Unified stock-maintenance pass. Iterates the provider's
    /// <see cref="StockTarget"/> list, compares each target's virtual stock
    /// (physical + in-flight BuyOrders) against the active
    /// <see cref="ILogisticsPolicy"/>, and places a BuyOrder for the quantity
    /// the policy asks for.
    /// </summary>
    // Reused scratch list for stock targets — avoids the per-call `ToList` allocation
    // documented in the audit (E). CheckStockTargets is only called on punch-in / OnNewDay
    // (not per tick), so the win here is small but free. See
    // wiki/projects/optimisation-backlog.md entry #2 / E.
    private readonly List<StockTarget> _scratchStockTargets = new List<StockTarget>(8);

    public void CheckStockTargets(IStockProvider provider, Character worker)
    {
        if (provider == null) return;

        string workerName = worker != null ? worker.CharacterName : "?";
        _scratchStockTargets.Clear();
        try
        {
            // Materialize into the reused scratch list so the iteration below can know
            // total count + index for diagnostic strings without paying a fresh List alloc.
            foreach (var target in provider.GetStockTargets())
            {
                _scratchStockTargets.Add(target);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[LogisticsStockEvaluator] {_building?.BuildingName}: provider.GetStockTargets() threw. Skipping stock-target pass this tick.");
            return;
        }

        int totalTargets = _scratchStockTargets.Count;

        if (_facade.LogLogisticsFlow)
        {
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> CheckStockTargets → {_building.BuildingName} has {totalTargets} stock target(s). Inspector '_logLogisticsFlow' is ON.");
        }

        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> {workerName} vérifie le stock cible de {_building.BuildingName}.");

        var policy = _facade.Policy;

        // Defensive iteration (rule #31): wrap the body in try/catch so a single item failing —
        // null supplier, broken policy, missing reference — doesn't abort the whole stock pass and
        // silently skip every subsequent item in the list. Without this wrapper, a NullRef on
        // _itemsToSell[1] would prevent _itemsToSell[2] from ever being checked, exactly matching
        // the "only orders the first item" symptom.
        for (int i = 0; i < totalTargets; i++)
        {
            var target = _scratchStockTargets[i];
            try
            {
                ProcessOneStockTarget(target, policy, i, totalTargets);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[LogisticsStockEvaluator] {_building?.BuildingName}: ProcessOneStockTarget threw for target index {i} (item='{target.ItemToStock?.ItemName}'). Continuing with next target.");
            }
        }

        _scratchStockTargets.Clear();
    }

    private void ProcessOneStockTarget(StockTarget target, ILogisticsPolicy policy, int index, int totalTargets)
    {
        ItemSO itemSO = target.ItemToStock;
        int minStock = target.MinStock;
        if (itemSO == null || minStock <= 0) return;

        int currentStock = _building.GetItemCount(itemSO);
        int alreadyOrdered = _orderBook.SumInFlightQuantityFor(itemSO);
        int virtualStock = currentStock + alreadyOrdered;

        int quantityToOrder = 0;
        try
        {
            quantityToOrder = policy.ComputeReorderQuantity(virtualStock, target);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[LogisticsStockEvaluator] {_building?.BuildingName}: policy '{(policy as Object)?.name ?? "<null>"}' threw for item '{itemSO.ItemName}'. Falling back to 0.");
            quantityToOrder = 0;
        }

        if (CommercialBuilding.DebugInventorySync)
        {
            Debug.Log($"<color=#aaccff>[StockCheck {index + 1}/{totalTargets}]</color> {_building.BuildingName} :: {itemSO.ItemName} (id='{itemSO.ItemId}') → physical={currentStock}, inFlight={alreadyOrdered}, virtual={virtualStock}, min={minStock}, policy→order={quantityToOrder}.");
        }

        if (quantityToOrder <= 0)
        {
            if (_facade.LogLogisticsFlow)
            {
                Debug.Log($"<color=#66ccff>[LogisticsDBG]</color>   ✓ {itemSO.ItemName}: virtual={virtualStock} (physical={currentStock}, inFlight={alreadyOrdered}) / min={minStock} — policy says no order.");
            }
            else
            {
                Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ✓ {itemSO.ItemName}: {virtualStock}/{minStock} (Virtuel) — stock suffisant.");
            }
            return;
        }

        if (_facade.LogLogisticsFlow)
        {
            Debug.Log($"<color=#ffcc66>[LogisticsDBG]</color>   ✗ {itemSO.ItemName}: virtual={virtualStock} (physical={currentStock}, inFlight={alreadyOrdered}) / min={minStock} — policy requests {quantityToOrder}, routing to supplier.");
        }
        else
        {
            Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color>   ✗ {itemSO.ItemName}: {virtualStock}/{minStock} (Virtuel) — stock bas, commande nécessaire...");
        }

        RequestStock(itemSO, quantityToOrder);
    }

    /// <summary>
    /// Commission-fulfilment pass for <see cref="CraftingBuilding"/>.
    /// Aggregates ingredient demand across every non-completed commissioned
    /// <see cref="CraftingOrder"/> and places BuyOrders for the shortfall.
    /// Distinct from <see cref="CheckStockTargets"/>: this serves external
    /// commissions, not idle stocking targets.
    /// </summary>
    public void CheckCraftingIngredients(CraftingBuilding building, Character worker = null)
    {
        if (building == null) return;

        var activeCraftingOrders = _orderBook.ActiveCraftingOrdersForIteration();

        if (_facade.LogLogisticsFlow)
        {
            int activeCount = activeCraftingOrders.Count(o => !o.IsCompleted);
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> CheckCraftingIngredients → {building.BuildingName} has {activeCount} active commission(s).");
        }

        var globalIngredientNeeds = new Dictionary<ItemSO, int>();

        foreach (var order in activeCraftingOrders)
        {
            if (order.IsCompleted) continue;
            var recipe = order.ItemToCraft.CraftingRecipe;
            if (recipe == null) continue;

            foreach (var ingredient in recipe)
            {
                if (!globalIngredientNeeds.ContainsKey(ingredient.Item))
                    globalIngredientNeeds[ingredient.Item] = 0;

                globalIngredientNeeds[ingredient.Item] += (ingredient.Amount * order.Quantity);
            }
        }

        foreach (var kvp in globalIngredientNeeds)
        {
            ItemSO itemSO = kvp.Key;
            int totalNeeded = kvp.Value;
            int possessed = building.GetItemCount(itemSO);
            int alreadyOrdered = _orderBook.SumInFlightQuantityFor(itemSO);
            int virtualStock = possessed + alreadyOrdered;

            if (virtualStock < totalNeeded)
            {
                int quantityToOrder = totalNeeded - virtualStock;
                Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color> Déficit global pour {itemSO.ItemName} : {virtualStock}/{totalNeeded} (Possédés:{possessed}, EnAttente:{alreadyOrdered}). Placement d'une commande pour {quantityToOrder}...");
                RequestStock(itemSO, quantityToOrder);
            }
        }
    }

    /// <summary>
    /// Attempts to route a BuyOrder for <paramref name="itemSO"/> × <paramref name="quantityToOrder"/>
    /// to the best-matching supplier. Dedupes against existing in-flight and
    /// in-queue orders (so tick-after-tick calls don't spam orders).
    ///
    /// Returns <c>true</c> if stock was sourced — either a B2B purchase committed, an
    /// existing in-flight or pending order absorbed the demand, or a new BuyOrder was
    /// placed. Returns <c>false</c> when no supplier of any tier (B2B, producer chain,
    /// or virtual) could fulfil the request — callers (e.g.
    /// <see cref="BuildingLogisticsManager.ProcessActiveBuildOrders"/>) use this signal
    /// to fall back to physical-harvest via the AB's unfulfillable-material queue.
    /// </summary>
    public bool RequestStock(ItemSO itemSO, int quantityToOrder)
    {
        if (itemSO == null || quantityToOrder <= 0) return false;

        // B2B preference scan (2026-05-09): before falling through to the producer-based
        // BuyOrder path, see if a same-map ShopBuilding sells this item, has the stock,
        // and the buyer's Treasury can afford it. On match the purchase is committed
        // atomically (debit treasury → credit shop till → move items from sell-shelf
        // into shop inventory → enqueue BuyOrder with IsPlaced=true). The standard
        // transporter dispatch then ships items from shop to buyer.
        if (TryB2BPurchaseFromShop(itemSO, quantityToOrder)) return true;

        var supplier = FindSupplierFor(itemSO);
        if (supplier == null)
        {
            if (_facade.LogLogisticsFlow)
            {
                Debug.Log($"<color=#ff8866>[LogisticsDBG]</color> RequestStock → NO supplier found for {itemSO.ItemName} (qty={quantityToOrder}). Building '{_building.BuildingName}' cannot restock this item.");
            }
            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color>   Aucun fournisseur trouvé pour {itemSO.ItemName}.");
            return false;
        }

        if (_facade.LogLogisticsFlow)
        {
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> RequestStock → supplier='{supplier.BuildingName}' found for {itemSO.ItemName} (qty={quantityToOrder}), building='{_building.BuildingName}'.");
        }

        var supplierLogistics = supplier.LogisticsManager;
        if (supplierLogistics == null)
        {
            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color>   {supplier.BuildingName} n'a pas de LogisticsManager assigné.");
            return false;
        }

        bool alreadyOrdered = supplierLogistics.ActiveOrders.Any(o => o.ItemToTransport == itemSO && o.Destination == _building);
        if (alreadyOrdered)
        {
            Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ⏳ {itemSO.ItemName}: BuyOrder déjà en cours chez {supplier.BuildingName}.");
            return true;
        }

        var pendingOrder = _orderBook.FindUnplacedBuyOrder(itemSO, supplier);
        if (pendingOrder != null)
        {
            Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ⏳ {itemSO.ItemName}: Une commande en attente existe! Ajout de la quantité (+{quantityToOrder}) à celle-ci.");
            pendingOrder.AddQuantity(quantityToOrder);
            return true;
        }

        var buyOrder = new BuyOrder(
            itemSO,
            quantityToOrder,
            supplier,
            _building,
            3,
            _building.Owner,
            null
        );

        _orderBook.AddPlacedBuyOrder(buyOrder);
        _orderBook.EnqueuePending(new BuildingLogisticsManager.PendingOrder(buyOrder, supplier));

        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   📦 Enregistrement d'une commande d'achat (BuyOrder) de {quantityToOrder}x {itemSO.ItemName} auprès de {supplier.BuildingName}.");
        return true;
    }

    /// <summary>
    /// First-match supplier lookup across every <see cref="CommercialBuilding"/>
    /// registered with the scene's <see cref="BuildingManager"/>. Future work:
    /// rank by distance, price, or reputation.
    /// </summary>
    public CommercialBuilding FindSupplierFor(ItemSO item)
    {
        if (item == null) return null;
        if (BuildingManager.Instance == null) return null;

        try
        {
            foreach (var b in BuildingManager.Instance.allBuildings)
            {
                if (b == _building || !(b is CommercialBuilding commBuilding)) continue;
                if (commBuilding.ProducesItem(item)) return commBuilding;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[LogisticsStockEvaluator] {_building?.BuildingName}: FindSupplierFor threw while iterating BuildingManager.allBuildings. Returning null.");
        }
        return null;
    }

    // ============================================================================
    // B2B SHOP PURCHASE (2026-05-09)
    //
    // Before posting a producer-based BuyOrder, see if a same-map ShopBuilding
    // can fulfil the demand from its existing sell-shelf stock. When a match
    // exists and the buyer's Treasury covers the cost:
    //   1. Atomically debit the buyer's Treasury (via the aggregator).
    //   2. Credit the shop's first Cashier till — symmetric with human/NPC
    //      personal purchases (Phase 2b chose Cashier till as the till
    //      destination for B2B too — see commercial-treasury.md design).
    //   3. Move the matched ItemInstances from the shop's sell-shelves into
    //      the shop's Inventory list. The standard
    //      LogisticsTransportDispatcher.ProcessActiveBuyOrders path picks them
    //      up from there (it scans _building.Inventory), creates a
    //      TransportOrder, and dispatches a transporter to ship them.
    //   4. Add the BuyOrder to BOTH order books (buyer side + shop side) with
    //      IsPlaced=true so the buyer-side GoapAction_PlaceOrder never runs
    //      for this order — "Background commit" was the locked design choice
    //      (no NPC walking to the shop to negotiate; the order is server-
    //      committed and the only physical movement is the transporter
    //      shipping the items).
    //
    // Refund-on-expiration is NOT implemented in this MVP — if the BuyOrder
    // expires via DecreaseRemainingDays before the transporter completes
    // delivery, the buyer's coins stay in the shop's till and the items stay
    // in the shop's inventory. Acceptable starting point; refund pass tracked
    // as follow-up in the Treasury wiki page.
    // ============================================================================

    /// <summary>
    /// Same-map ShopBuilding scan + atomic B2B commit. Returns <c>true</c> when an order
    /// was successfully placed (consumer of <see cref="RequestStock"/> short-circuits the
    /// producer-based path). Returns <c>false</c> when no shop match was found or the
    /// Treasury can't afford the cheapest one.
    /// </summary>
    private bool TryB2BPurchaseFromShop(ItemSO itemSO, int quantityToOrder)
    {
        if (itemSO == null || quantityToOrder <= 0) return false;
        if (BuildingManager.Instance == null) return false;
        if (_building == null) return false;

        var currency = MWI.Economy.CurrencyId.Default;
        bool logFlow = _facade != null && _facade.LogLogisticsFlow;

        // Cache buyer's map id once for the same-map filter. Building doesn't expose a
        // direct MapId property; the canonical resolver is GetComponentInParent<MapController>
        // (mirrors the pattern used in CommercialBuilding.StampOriginIds).
        var buyerMapController = _building.GetComponentInParent<MWI.WorldSystem.MapController>();
        string buyerMapId = buyerMapController != null ? buyerMapController.MapId : string.Empty;
        if (string.IsNullOrEmpty(buyerMapId))
        {
            if (logFlow)
            {
                Debug.Log($"<color=#ff8866>[LogisticsDBG]</color> B2B → buyer '{_building.BuildingName}' has no MapController parent; skipping shop scan.");
            }
            return false;
        }

        try
        {
            // === Pass 1: collect qualifying shops ===
            // Project convention (2026-05-17d): every NPC-buys-from-building decision
            // is reputation-weighted via ReputationWeightedPicker. The B2B procurement
            // path mirrors the customer-NPC pattern in GoapAction_GoShopping —
            // qualify first (sells / stock / rep ≥ B2B floor / buyer can afford /
            // cashier present), then pick one weighted by max(10, rep).
            var qualifiers = new List<ShopBuilding>();
            foreach (var b in BuildingManager.Instance.allBuildings)
            {
                if (b == null || b == _building) continue;
                if (!(b is ShopBuilding shop)) continue;

                // Same-map scope (locked product decision 2026-05-09).
                var shopMapController = shop.GetComponentInParent<MWI.WorldSystem.MapController>();
                string shopMapId = shopMapController != null ? shopMapController.MapId : string.Empty;
                if (!string.Equals(shopMapId, buyerMapId)) continue;

                // Reputation hard floor (2026-05-16). Shops below ReputationB2BMinimum
                // are invisible to procurement — they've failed enough recent deliveries
                // that the supply chain refuses to source from them. Recovers naturally
                // as the shop completes future orders (+1 per successful delivery; see
                // commercial-treasury.md). Customers (GoapAction_GoShopping) don't apply
                // this hard floor — they only weight, never exclude.
                if (shop.Reputation < CommercialBuilding.ReputationB2BMinimum)
                {
                    if (logFlow)
                    {
                        Debug.Log($"<color=#ff8866>[LogisticsDBG]</color> B2B → shop '{shop.BuildingName}' skipped: reputation {shop.Reputation} < {CommercialBuilding.ReputationB2BMinimum}.");
                    }
                    continue;
                }

                var catalogEntry = shop.GetCatalogEntry(itemSO);
                if (!catalogEntry.HasValue) continue; // shop doesn't sell this item

                int stock = CountItemOnSellShelves(shop, itemSO);
                if (stock < quantityToOrder) continue; // not enough stock

                int unitPrice = ShopBuilding.ResolvePrice(catalogEntry.Value);
                int totalCost = unitPrice * quantityToOrder;
                if (totalCost <= 0) continue; // free item — fall through to producer path

                if (!_building.CanAffordFromTreasury(currency, totalCost))
                {
                    if (logFlow)
                    {
                        Debug.Log($"<color=#ff8866>[LogisticsDBG]</color> B2B → shop '{shop.BuildingName}' has {stock}× {itemSO.ItemName} (need {quantityToOrder} @ {unitPrice}g = {totalCost}g) but treasury={_building.GetTreasuryBalance(currency)}g. Skipping.");
                    }
                    continue;
                }

                // Cashier must exist (will receive the till credit).
                var cashierProbe = (shop.Cashiers != null && shop.Cashiers.Count > 0) ? shop.Cashiers[0] : null;
                if (cashierProbe == null)
                {
                    if (logFlow)
                    {
                        Debug.LogWarning($"[LogisticsDBG] B2B → shop '{shop.BuildingName}' has stock + buyer can afford, but shop has no Cashier to receive till credit. Skipping.");
                    }
                    continue;
                }

                qualifiers.Add(shop);
            }

            if (qualifiers.Count == 0) return false;

            // === Pass 2: reputation-weighted pick ===
            // Per the project convention. Single-qualifier fast-path inside the helper.
            var pickedShop = ReputationWeightedPicker.Pick(qualifiers);
            if (pickedShop == null) return false;

            // === Pass 3: atomic commit on the picked shop ===
            // Re-derive the per-shop values (we threw them away after Pass 1 to avoid
            // a tuple-list allocation; recompute is O(1) cashier/catalog lookups).
            var pickedEntry  = pickedShop.GetCatalogEntry(itemSO);
            var pickedCashier = pickedShop.Cashiers[0];
            int pickedUnit = ShopBuilding.ResolvePrice(pickedEntry.Value);
            int pickedTotal = pickedUnit * quantityToOrder;

            if (!_building.TryDebitTreasury(currency, pickedTotal,
                    $"B2B_Purchase_{itemSO.ItemName}_x{quantityToOrder}_from_{pickedShop.BuildingName}"))
            {
                // Shouldn't happen — CanAffordFromTreasury guarded Pass 1.
                Debug.LogError($"[LogisticsStockEvaluator] {_building.BuildingName}: B2B treasury debit failed AFTER CanAffordFromTreasury returned true (item={itemSO.ItemName}, total={pickedTotal}). Treasury state may be inconsistent.");
                return false;
            }
            pickedCashier.CreditTill(currency, pickedTotal,
                $"B2B_PurchaseFrom_{_building.BuildingName}_{itemSO.ItemName}_x{quantityToOrder}");

            int movedCount = MoveSellShelfItemsToShopInventory(pickedShop, itemSO, quantityToOrder);
            if (movedCount < quantityToOrder)
            {
                int shortfall = quantityToOrder - movedCount;
                int refund = shortfall * pickedUnit;
                Debug.LogWarning($"[LogisticsStockEvaluator] {_building.BuildingName}: B2B race detected — committed for {quantityToOrder} but only {movedCount} items moved. Refunding {refund}g.");
                pickedCashier.DebitTill(currency, refund, $"B2B_Refund_{_building.BuildingName}_partial");
                _building.CreditTreasury(currency, refund, $"B2B_Refund_partial_from_{pickedShop.BuildingName}");
                if (movedCount <= 0)
                {
                    // Total race — picked shop lost all its qualifying stock between Pass 1
                    // and Pass 3. Producer-path fallback runs on the next RequestStock tick.
                    return false;
                }
                quantityToOrder = movedCount;
                pickedTotal = quantityToOrder * pickedUnit;
            }

            var buyOrder = new BuyOrder(itemSO, quantityToOrder, pickedShop, _building, 3, _building.Owner, null);
            buyOrder.IsPlaced = true;

            _orderBook.AddPlacedBuyOrder(buyOrder);
            if (pickedShop.LogisticsManager != null)
            {
                pickedShop.LogisticsManager.OrderBook.AddActiveOrder(buyOrder);
            }
            else
            {
                Debug.LogWarning($"[LogisticsStockEvaluator] {_building.BuildingName}: B2B shop '{pickedShop.BuildingName}' has no LogisticsManager — its dispatcher will not run. Order added to buyer side only; transporter may not ship.");
            }

            Debug.Log($"<color=#aaffaa>[B2B]</color> {_building.BuildingName} ← {pickedShop.BuildingName}: bought {quantityToOrder}× {itemSO.ItemName} for {pickedTotal}g (unit={pickedUnit}g, rep={pickedShop.Reputation}, qualifiers={qualifiers.Count}). IsPlaced=true, awaiting transport.");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[LogisticsStockEvaluator] {_building.BuildingName}: TryB2BPurchaseFromShop threw while scanning shops for {itemSO?.ItemName}. Falling through to producer path.");
            return false;
        }

        return false;
    }

    private static int CountItemOnSellShelves(ShopBuilding shop, ItemSO itemSO)
    {
        if (shop == null || itemSO == null) return 0;
        var shelves = shop.SellShelves;
        if (shelves == null) return 0;
        int count = 0;
        for (int i = 0; i < shelves.Count; i++)
        {
            var shelf = shelves[i];
            if (shelf == null) continue;
            int cap = shelf.Capacity;
            for (int s = 0; s < cap; s++)
            {
                var slot = shelf.GetItemSlot(s);
                if (slot == null || slot.IsEmpty()) continue;
                if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == itemSO) count++;
            }
        }
        return count;
    }

    private static int MoveSellShelfItemsToShopInventory(ShopBuilding shop, ItemSO itemSO, int desired)
    {
        if (shop == null || itemSO == null || desired <= 0) return 0;
        var shelves = shop.SellShelves;
        if (shelves == null) return 0;
        int moved = 0;
        for (int i = 0; i < shelves.Count && moved < desired; i++)
        {
            var shelf = shelves[i];
            if (shelf == null) continue;
            int cap = shelf.Capacity;
            for (int s = 0; s < cap && moved < desired; s++)
            {
                var slot = shelf.GetItemSlot(s);
                if (slot == null || slot.IsEmpty()) continue;
                if (slot.ItemInstance == null || slot.ItemInstance.ItemSO != itemSO) continue;
                var inst = slot.ItemInstance;
                if (shelf.RemoveItem(inst))
                {
                    shop.AddToInventory(inst);
                    moved++;
                }
            }
        }
        return moved;
    }
}
