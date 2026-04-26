// Assets/Scripts/Character/CharacterOrders/OrderImmediate.cs
namespace MWI.Orders
{
    /// <summary>
    /// Order with no objective tracked in the quest log; the receiver's behavior is
    /// polled directly via IsComplied(). Examples: "Leave this area", "Drop your weapon",
    /// "Halt", "Stop attacking".
    /// </summary>
    public abstract class OrderImmediate : Order
    {
        // No quest log integration. Resolution = whether IsComplied() returns true
        // before the timeout expires.
        public override void OnAccepted() { /* no-op */ }
        public override void OnResolved(OrderState finalState) { /* no log cleanup */ }
    }
}
