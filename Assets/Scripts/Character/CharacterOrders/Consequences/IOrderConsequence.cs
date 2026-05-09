namespace MWI.Orders
{
    /// <summary>
    /// Server-side strategy fired when an order resolves as Disobeyed (refused or timed out).
    /// Each implementation is a ScriptableObject; SoName is the stable filename used as the
    /// network identifier in OrdersSaveData and OrderSyncData payload metadata.
    ///
    /// Implementations MUST handle null issuer gracefully (no-op for issuer-dependent effects
    /// like RelationDrop or IssuerAttacks).
    /// </summary>
    public interface IOrderConsequence
    {
        /// <summary>Stable identifier; should equal the SO asset filename without extension.</summary>
        string SoName { get; }

        /// <summary>Server-only. Called once when the order resolves as Disobeyed.</summary>
        void Apply(Order order, Character receiver, IOrderIssuer issuer);
    }
}
