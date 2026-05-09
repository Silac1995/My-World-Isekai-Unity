using UnityEngine;

/// <summary>
/// Base ScriptableObject asset for <see cref="ILogisticsPolicy"/> implementations.
/// Enables designers to drop a policy asset directly onto a
/// <see cref="BuildingLogisticsManager"/> in the Inspector.
/// </summary>
public abstract class LogisticsPolicy : ScriptableObject, ILogisticsPolicy
{
    /// <inheritdoc/>
    public abstract int ComputeReorderQuantity(int currentVirtualStock, StockTarget target);
}
