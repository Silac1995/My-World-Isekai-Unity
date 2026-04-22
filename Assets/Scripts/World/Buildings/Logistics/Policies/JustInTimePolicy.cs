using UnityEngine;

/// <summary>
/// Just-In-Time policy: whenever virtual stock is below MinStock, order
/// one fixed batch (<see cref="_batchSize"/>) at a time — never the full
/// shortfall. Good for expensive items where over-ordering hurts.
///
/// Example: MinStock=10, _batchSize=1
///   current=0 → order 1
///   (next tick, 1 in-flight → virtual=1 → still short → order 1 more)
///   … continues tick-by-tick until virtual >= MinStock.
/// </summary>
[CreateAssetMenu(menuName = "MWI/Logistics/Policy/JustInTime", fileName = "JustInTimePolicy")]
public class JustInTimePolicy : LogisticsPolicy
{
    [Tooltip("Number of units to order per tick when stock is below MinStock. " +
             "Clamped against the actual shortfall so we never overshoot.")]
    [SerializeField, Min(1)] private int _batchSize = 1;

    /// <inheritdoc/>
    public override int ComputeReorderQuantity(int currentVirtualStock, StockTarget target)
    {
        if (target.MinStock <= 0) return 0;
        if (currentVirtualStock >= target.MinStock) return 0;

        int shortfall = target.MinStock - currentVirtualStock;
        // Never order more than the actual shortfall — batchSize is an upper bound.
        int quantityToOrder = Mathf.Min(_batchSize, shortfall);
        return quantityToOrder > 0 ? quantityToOrder : 0;
    }
}
