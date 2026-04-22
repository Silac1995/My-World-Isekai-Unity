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
    public void CheckStockTargets(IStockProvider provider, Character worker)
    {
        if (provider == null) return;

        string workerName = worker != null ? worker.CharacterName : "?";
        List<StockTarget> targets;
        try
        {
            targets = provider.GetStockTargets().ToList();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[LogisticsStockEvaluator] {_building?.BuildingName}: provider.GetStockTargets() threw. Skipping stock-target pass this tick.");
            return;
        }

        if (_facade.LogLogisticsFlow)
        {
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> CheckStockTargets → {_building.BuildingName} has {targets.Count} stock target(s). Inspector '_logLogisticsFlow' is ON.");
        }

        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> {workerName} vérifie le stock cible de {_building.BuildingName}.");

        var policy = _facade.Policy;

        foreach (var target in targets)
        {
            ItemSO itemSO = target.ItemToStock;
            int minStock = target.MinStock;
            if (itemSO == null || minStock <= 0) continue;

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
                continue;
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
    /// </summary>
    public void RequestStock(ItemSO itemSO, int quantityToOrder)
    {
        if (itemSO == null || quantityToOrder <= 0) return;

        var supplier = FindSupplierFor(itemSO);
        if (supplier == null)
        {
            if (_facade.LogLogisticsFlow)
            {
                Debug.Log($"<color=#ff8866>[LogisticsDBG]</color> RequestStock → NO supplier found for {itemSO.ItemName} (qty={quantityToOrder}). Building '{_building.BuildingName}' cannot restock this item.");
            }
            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color>   Aucun fournisseur trouvé pour {itemSO.ItemName}.");
            return;
        }

        if (_facade.LogLogisticsFlow)
        {
            Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> RequestStock → supplier='{supplier.BuildingName}' found for {itemSO.ItemName} (qty={quantityToOrder}), building='{_building.BuildingName}'.");
        }

        var supplierLogistics = supplier.LogisticsManager;
        if (supplierLogistics == null)
        {
            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color>   {supplier.BuildingName} n'a pas de LogisticsManager assigné.");
            return;
        }

        bool alreadyOrdered = supplierLogistics.ActiveOrders.Any(o => o.ItemToTransport == itemSO && o.Destination == _building);
        if (alreadyOrdered)
        {
            Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ⏳ {itemSO.ItemName}: BuyOrder déjà en cours chez {supplier.BuildingName}.");
            return;
        }

        var pendingOrder = _orderBook.FindUnplacedBuyOrder(itemSO, supplier);
        if (pendingOrder != null)
        {
            Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ⏳ {itemSO.ItemName}: Une commande en attente existe! Ajout de la quantité (+{quantityToOrder}) à celle-ci.");
            pendingOrder.AddQuantity(quantityToOrder);
            return;
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
}
