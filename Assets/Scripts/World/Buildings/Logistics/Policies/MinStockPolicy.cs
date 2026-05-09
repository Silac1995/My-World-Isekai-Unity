using UnityEngine;

/// <summary>
/// Default stocking policy. Byte-identical to the hardcoded behaviour that
/// shipped in Layers A+B: whenever virtual stock drops below MinStock,
/// order exactly enough to refill up to MinStock.
///
/// Example: MinStock=10, current=3 → order 7.
/// </summary>
[CreateAssetMenu(menuName = "MWI/Logistics/Policy/MinStock", fileName = "MinStockPolicy")]
public class MinStockPolicy : LogisticsPolicy
{
    /// <inheritdoc/>
    public override int ComputeReorderQuantity(int currentVirtualStock, StockTarget target)
    {
        if (target.MinStock <= 0) return 0;
        if (currentVirtualStock >= target.MinStock) return 0;

        int shortfall = target.MinStock - currentVirtualStock;
        return shortfall > 0 ? shortfall : 0;
    }
}
