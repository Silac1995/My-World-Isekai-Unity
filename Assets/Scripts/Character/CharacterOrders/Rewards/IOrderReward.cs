namespace MWI.Orders
{
    /// <summary>
    /// Server-side strategy fired when an order resolves as Complied. Symmetric to
    /// IOrderConsequence. Each implementation is a ScriptableObject.
    /// Implementations MUST handle null issuer gracefully.
    /// </summary>
    public interface IOrderReward
    {
        string SoName { get; }
        void Apply(Order order, Character receiver, IOrderIssuer issuer);
    }
}
