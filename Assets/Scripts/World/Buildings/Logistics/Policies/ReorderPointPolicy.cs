using UnityEngine;

/// <summary>
/// Classic reorder-point policy: do nothing until stock falls below a
/// fraction of MinStock, then batch-order up to a multiple of MinStock.
/// Lets designers express "only reorder when I'm down to 50%, and when
/// I do, stock up to 200%".
///
/// Example: MinStock=10, _reorderThresholdPct=0.5, _orderMultiplier=2.0
///   current=8  → 8 >= 5   → 0 (no order)
///   current=4  → 4 <  5   → order (10 * 2.0) - 4 = 16
/// </summary>
[CreateAssetMenu(menuName = "MWI/Logistics/Policy/ReorderPoint", fileName = "ReorderPointPolicy")]
public class ReorderPointPolicy : LogisticsPolicy
{
    [Tooltip("Fraction of MinStock below which a reorder fires. 0.5 = reorder at 50% of MinStock.")]
    [SerializeField, Range(0.01f, 1f)] private float _reorderThresholdPct = 0.5f;

    [Tooltip("Multiplier applied to MinStock when an order fires. 2.0 = refill up to 200% of MinStock.")]
    [SerializeField, Min(1f)] private float _orderMultiplier = 2f;

    /// <inheritdoc/>
    public override int ComputeReorderQuantity(int currentVirtualStock, StockTarget target)
    {
        if (target.MinStock <= 0) return 0;

        // Reorder point: we only fire once stock falls under this threshold.
        float reorderPoint = target.MinStock * _reorderThresholdPct;
        if (currentVirtualStock >= reorderPoint) return 0;

        // Target quantity: refill up to OrderMultiplier * MinStock.
        int targetQuantity = Mathf.CeilToInt(target.MinStock * _orderMultiplier);
        int quantityToOrder = targetQuantity - currentVirtualStock;

        return quantityToOrder > 0 ? quantityToOrder : 0;
    }
}
