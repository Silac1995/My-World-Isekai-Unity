using System.Collections.Generic;

/// <summary>
/// Save-data shape for a <see cref="Cashier"/> furniture instance — round-tripped on world
/// save/load through <c>BuildingSaveData.Cashiers</c>. Captures only the gameplay-relevant
/// state that cannot be re-derived from the prefab:
/// <list type="bullet">
/// <item><description><c>till</c> — per-currency coin balance (copy of <c>Cashier._till</c>).</description></item>
/// <item><description><c>linkedBuildingId</c> — UUID of the parent <see cref="CommercialBuilding"/>,
/// for late-bind diagnostics; runtime parent resolution still goes through
/// <c>GetComponentInParent&lt;CommercialBuilding&gt;()</c> (this field is informational).</description></item>
/// <item><description><c>requiresVendor</c> — Inspector-authored flag. Saved for completeness;
/// the live value is restored from the prefab's serialized field on respawn.</description></item>
/// </list>
///
/// <para>
/// Sibling type to <see cref="CurrencyBalanceEntry"/> (a <see cref="System.Serializable"/>
/// save-data class living in <c>WalletSaveData.cs</c>). NOT to be confused with
/// <see cref="CashierTillEntry"/>, which is the wire-format <see cref="Unity.Netcode.INetworkSerializable"/>
/// struct in <c>CashierNetSync.cs</c>.
/// </para>
/// </summary>
[System.Serializable]
public class CashierSaveData
{
    public List<CurrencyBalanceEntry> till = new();
    public string linkedBuildingId;
    public bool requiresVendor;
}
