using System.Collections.Generic;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Abstract server-side runtime object representing an issued directive.
    /// Lives only on the server; clients see OrderSyncData snapshots.
    /// See spec §5 for the full data model.
    /// </summary>
    public abstract class Order
    {
        // Identity
        public ulong  OrderId;
        public string OrderTypeName;            // Stable name, e.g. "Order_Kill"

        // Parties
        public IOrderIssuer Issuer;             // Nullable
        public Character    Receiver;           // Always non-null

        // Authority + priority
        public AuthorityContextSO AuthorityContext;
        public OrderUrgency       Urgency;
        public int                Priority;

        // Lifetime
        public float      TimeoutSeconds;
        public float      ElapsedSeconds;
        public OrderState State;

        // Composition
        public List<IOrderConsequence> Consequences = new();
        public List<IOrderReward>      Rewards      = new();

        // ── Lifecycle hooks (overridden by concrete subclasses) ──

        /// <summary>Pre-flight validity check. Called on the server before issuance.</summary>
        public abstract bool CanIssueAgainst(Character receiver);

        /// <summary>Called immediately after the receiver accepts. Used by OrderQuest to register with the quest log.</summary>
        public abstract void OnAccepted();

        /// <summary>Server-side polled check: did the receiver actually do the thing?</summary>
        public abstract bool IsComplied();

        /// <summary>Called every server frame while State == Active.</summary>
        public virtual void OnTick(float dt)
        {
            ElapsedSeconds += dt;
        }

        /// <summary>Called when the order resolves (Complied / Disobeyed / Cancelled). Used by OrderQuest to clean up the quest log.</summary>
        public abstract void OnResolved(OrderState finalState);

        /// <summary>Type-specific payload (target id, zone center, etc.) → bytes for sync + save.</summary>
        public abstract byte[] SerializeOrderPayload();

        /// <summary>Type-specific payload bytes → fields. Called on save reload.</summary>
        public abstract void DeserializeOrderPayload(byte[] data);

        /// <summary>
        /// GOAP world-state precondition this order needs satisfied. Keys are GOAP world-state
        /// flags; values are the bool the planner needs them to equal. e.g. { "TargetIsDead_42": true }.
        ///
        /// The current GOAP layer (CharacterGoapController) does not yet consume this directly —
        /// hooking it requires (a) injecting an order-derived GoapGoal into Replan() alongside
        /// need-derived goals, and (b) adding GoapAction(s) whose Effects satisfy these dynamic
        /// keys. Both are deferred to a follow-up GOAP integration pass.
        /// </summary>
        public abstract Dictionary<string, bool> GetGoapPrecondition();
    }
}
