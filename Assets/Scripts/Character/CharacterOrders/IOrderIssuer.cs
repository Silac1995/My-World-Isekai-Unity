namespace MWI.Orders
{
    /// <summary>
    /// Anything that can issue an Order. v1 implementation: Character.
    /// Future: Faction, BuildingOwnerProxy, environmental rules.
    /// Nullable in callers — Order accepts a null issuer for anonymous/system orders.
    /// </summary>
    public interface IOrderIssuer
    {
        /// <summary>Returns the Character behind this issuer, or null for non-character issuers.</summary>
        Character AsCharacter { get; }

        /// <summary>Display name shown in UI / logs.</summary>
        string DisplayName { get; }

        /// <summary>Stable network identifier (NetworkObjectId for characters, 0 for anonymous).</summary>
        ulong IssuerNetId { get; }
    }
}
