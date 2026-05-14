using System;
using Unity.Netcode;

/// <summary>
/// Replicated treasury entry for the per-building Treasury system on
/// <see cref="CommercialBuilding"/>. Wire-format struct (INetworkSerializable +
/// IEquatable) — sibling to (but distinct from) <see cref="CurrencyBalanceEntry"/>
/// which is the save-data plain-Serializable class.
///
/// Mirrors the pattern established by <see cref="CashierTillEntry"/> on the
/// cashier till. Both will converge once the wallet / treasury / till systems
/// share a single replicated-currency type — out of scope for the B2B
/// shop-buy work (2026-05-09).
/// </summary>
public struct BuildingTreasuryEntry : INetworkSerializable, IEquatable<BuildingTreasuryEntry>
{
    public int CurrencyId;
    public int Amount;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CurrencyId);
        serializer.SerializeValue(ref Amount);
    }

    public bool Equals(BuildingTreasuryEntry other) =>
        CurrencyId == other.CurrencyId && Amount == other.Amount;
}
