// Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Server-authoritative subsystem managing all orders for a single Character.
    /// Both issuer-side (orders this character has issued) and receiver-side (orders
    /// this character has been given). See spec §5 for the public surface and §6 for
    /// the lifecycle.
    /// </summary>
    public class CharacterOrders : CharacterSystem, ICharacterSaveData<OrdersSaveData>
    {
        // ── Inspector ─────────────────────────────────────────────
        [Header("Settings")]
        [Tooltip("Time the receiver 'thinks' before responding to an order (NPC only). Mirrors CharacterInvitation._responseDelay.")]
        [SerializeField] private float _responseDelay = 1.0f;

        [Tooltip("How often (seconds) the server polls IsComplied() on each active order.")]
        [SerializeField] private float _compliancePollInterval = 0.5f;

        // ── Server-side runtime state ─────────────────────────────
        // Live Order instances exist only on the server; clients see *Sync* lists.
        private readonly List<Order> _activeOrdersServer  = new();   // Receiver-side
        private readonly List<Order> _issuedOrdersServer  = new();   // Issuer-side ledger
        private readonly Dictionary<ulong, Order> _ordersByIdServer = new();

        // ── Networked state ───────────────────────────────────────
        private NetworkList<OrderSyncData>        _activeOrdersSync;
        private NetworkList<PendingOrderSyncData> _pendingOrdersSync;

        // ── Server-side bookkeeping ───────────────────────────────
        private ulong _nextOrderIdServer = 1;
        private float _pollAccumulator;

        // ── Events ────────────────────────────────────────────────
        public event Action<Order>             OnOrderReceived;
        public event Action<Order>             OnOrderAccepted;
        public event Action<Order, OrderState> OnOrderResolved;

        // ── Public read-only accessors (server) ───────────────────
        public IReadOnlyList<Order> ActiveOrders => _activeOrdersServer;
        public IReadOnlyList<Order> IssuedOrders => _issuedOrdersServer;

        // ── Public read-only accessors (client) ───────────────────
        /// <summary>Snapshot list visible to all clients.</summary>
        public IReadOnlyList<OrderSyncData> ActiveOrdersSync        => GetSyncList(_activeOrdersSync);
        public IReadOnlyList<PendingOrderSyncData> PendingOrdersSync => GetSyncList(_pendingOrdersSync);

        private static IReadOnlyList<T> GetSyncList<T>(NetworkList<T> list) where T : unmanaged, INetworkSerializable, IEquatable<T>
        {
            if (list == null) return Array.Empty<T>();
            var copy = new T[list.Count];
            for (int i = 0; i < list.Count; i++) copy[i] = list[i];
            return copy;
        }

        // ── Lifecycle ─────────────────────────────────────────────
        protected override void Awake()
        {
            base.Awake();
            _activeOrdersSync  = new NetworkList<OrderSyncData>();
            _pendingOrdersSync = new NetworkList<PendingOrderSyncData>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _activeOrdersSync.OnListChanged  += OnActiveOrdersSyncChanged;
            _pendingOrdersSync.OnListChanged += OnPendingOrdersSyncChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (_activeOrdersSync != null)  _activeOrdersSync.OnListChanged  -= OnActiveOrdersSyncChanged;
            if (_pendingOrdersSync != null) _pendingOrdersSync.OnListChanged -= OnPendingOrdersSyncChanged;
        }

        private void OnActiveOrdersSyncChanged(NetworkListEvent<OrderSyncData> evt)
        {
            // Client-side render hook. Wired in later tasks (UI + quest log entry).
        }

        private void OnPendingOrdersSyncChanged(NetworkListEvent<PendingOrderSyncData> evt)
        {
            // Client-side render hook for the pending popup. Wired in later tasks.
        }

        private void Update()
        {
            if (!IsServer) return;
            // Server-side ticking implemented in Task 20.
        }

        // ── ICharacterSaveData<OrdersSaveData> ────────────────────
        // Stubs to satisfy the interface; full impls in Task 33+34.
        public string SaveKey      => "CharacterOrders";
        public int    LoadPriority => 60;

        public OrdersSaveData Serialize() => new OrdersSaveData();
        public void           Deserialize(OrdersSaveData data) { /* Task 33+34 */ }

        string ICharacterSaveData.SerializeToJson()                => CharacterSaveDataHelper.SerializeToJson(this);
        void   ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

        // ── Public surface that callers need from day one (full impls in later tasks) ──
        /// <summary>Server-side helper to issue an Order. Returns the new OrderId. Implemented in Task 20.</summary>
        public ulong IssueOrder(Order order)
        {
            Debug.LogWarning("[CharacterOrders] IssueOrder called but not yet implemented (Task 20).");
            return 0;
        }
    }
}
