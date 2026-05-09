/// <summary>
/// Authorable stocking strategy for a <see cref="BuildingLogisticsManager"/>.
/// Given the building's current virtual stock (physical + in-flight) and a
/// declared <see cref="StockTarget"/>, returns the quantity that should be
/// ordered right now (0 means "do not order at this time").
///
/// Implementations are expected to be ScriptableObject assets so designers
/// can author them per-prefab without code changes. See <see cref="LogisticsPolicy"/>
/// for the base class shipped alongside <c>MinStockPolicy</c>,
/// <c>ReorderPointPolicy</c> and <c>JustInTimePolicy</c>.
/// </summary>
public interface ILogisticsPolicy
{
    /// <summary>
    /// Compute how many units to order right now.
    /// </summary>
    /// <param name="currentVirtualStock">Physical inventory + any in-flight BuyOrders for the same item.</param>
    /// <param name="target">The declared stock target (item + MinStock).</param>
    /// <returns>Quantity to order (0 = skip this cycle). Must be non-negative.</returns>
    int ComputeReorderQuantity(int currentVirtualStock, StockTarget target);
}
