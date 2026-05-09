namespace MWI.Orders
{
    /// <summary>
    /// Lifecycle state of an Order. See Order state machine in spec §6.
    /// </summary>
    public enum OrderState : byte
    {
        Pending   = 0, // In _pendingOrdersSync, evaluation coroutine running
        Accepted  = 1, // Transient — immediately becomes Active
        Active    = 2, // In _activeOrdersSync, OnTick + IsComplied polling
        Complied  = 3, // Resolved successfully — rewards fired
        Disobeyed = 4, // Resolved by refusal or timeout — consequences fired
        Cancelled = 5, // Cancelled by issuer — no consequences, no rewards
    }

    /// <summary>
    /// Urgency modifier added to AuthorityContext.BasePriority to compute final Priority.
    /// </summary>
    public enum OrderUrgency : byte
    {
        Routine   = 0,
        Important = 15,
        Urgent    = 25,
        Critical  = 35,
    }
}
