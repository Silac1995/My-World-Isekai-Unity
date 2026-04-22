using System.Collections.Generic;

/// <summary>
/// Serializable (ItemSO, MinStock) pair used by commercial buildings to declare
/// a restocking target. The logistics manager polls these on every worker punch-in
/// and places <see cref="BuyOrder"/>s whenever the virtual stock (physical + in-flight)
/// drops below <see cref="MinStock"/>.
///
/// Semantics:
/// - For a ShopBuilding, MinStock == shelf target quantity (refill up to this value).
/// - For a CraftingBuilding, MinStock == minimum input material to keep on hand.
/// </summary>
[System.Serializable]
public struct StockTarget
{
    public ItemSO ItemToStock;
    public int MinStock;

    public StockTarget(ItemSO itemToStock, int minStock)
    {
        ItemToStock = itemToStock;
        MinStock = minStock;
    }
}

/// <summary>
/// Implemented by any <see cref="CommercialBuilding"/> that declares a set of
/// items it wants to keep in stock. Feeds <see cref="BuildingLogisticsManager.CheckStockTargets"/>.
/// </summary>
public interface IStockProvider
{
    /// <summary>
    /// Returns all stock targets this building wants maintained.
    /// Order is not significant. May yield zero targets (building doesn't restock autonomously).
    /// </summary>
    IEnumerable<StockTarget> GetStockTargets();
}
