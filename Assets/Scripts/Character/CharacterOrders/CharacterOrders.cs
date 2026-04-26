// Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
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

        // ── ICharacterSaveData<OrdersSaveData> ────────────────────
        // Stubs to satisfy the interface; full impls in Task 33+34.
        public string SaveKey      => "CharacterOrders";
        public int    LoadPriority => 60;

        public OrdersSaveData Serialize() => new OrdersSaveData();
        public void           Deserialize(OrdersSaveData data) { /* Task 33+34 */ }

        string ICharacterSaveData.SerializeToJson()                => CharacterSaveDataHelper.SerializeToJson(this);
        void   ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

        // ── Issuance (server-side) ───────────────────────────────────────
        /// <summary>Server-side helper to issue an Order. Returns the new OrderId, or 0 on rejection.</summary>
        public ulong IssueOrder(Order order)
        {
            if (!IsServer)
            {
                Debug.LogError("[CharacterOrders] IssueOrder called on non-server. This subsystem is server-authoritative.");
                return 0;
            }
            if (order == null || order.Receiver == null)
            {
                Debug.LogError("[CharacterOrders] IssueOrder called with null order or receiver.");
                return 0;
            }

            try
            {
                if (!order.CanIssueAgainst(order.Receiver))
                {
                    Debug.Log($"<color=orange>[Order]</color> {order.OrderTypeName} rejected: CanIssueAgainst returned false (issuer={order.Issuer?.DisplayName ?? "anonymous"}, receiver={order.Receiver.CharacterName}).");
                    return 0;
                }

                // Proximity check (long-range orders deferred to v2)
                bool requiresProximity = !(order.AuthorityContext != null && order.AuthorityContext.BypassProximity);
                if (requiresProximity && order.Issuer != null && order.Issuer.AsCharacter != null)
                {
                    var issuerChar = order.Issuer.AsCharacter;
                    if (order.Receiver.CharacterInteractable == null
                        || !order.Receiver.CharacterInteractable.IsCharacterInInteractionZone(issuerChar))
                    {
                        Debug.Log($"<color=orange>[Order]</color> {order.OrderTypeName} rejected: issuer {issuerChar.CharacterName} not in receiver's interaction zone.");
                        return 0;
                    }
                }

                order.OrderId = _nextOrderIdServer++;
                order.State = OrderState.Pending;

                var receiverOrders = order.Receiver.CharacterOrders;
                if (receiverOrders == null)
                {
                    Debug.LogError($"[Order] Receiver {order.Receiver.CharacterName} has no CharacterOrders subsystem.");
                    return 0;
                }
                receiverOrders.ReceiveOrder(order);

                // Track on the issuer side (this CharacterOrders instance might be the receiver itself for self-issued orders)
                if (order.Issuer != null && order.Issuer.AsCharacter != null)
                {
                    var issuerOrders = order.Issuer.AsCharacter.CharacterOrders;
                    if (issuerOrders != null)
                    {
                        issuerOrders._issuedOrdersServer.Add(order);
                        issuerOrders._ordersByIdServer[order.OrderId] = order;
                    }
                }

                return order.OrderId;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }

        /// <summary>Server-side: receive an order and start evaluation.</summary>
        internal void ReceiveOrder(Order order)
        {
            if (!IsServer) return;
            try
            {
                _ordersByIdServer[order.OrderId] = order;

                _pendingOrdersSync.Add(new PendingOrderSyncData
                {
                    OrderId        = order.OrderId,
                    IssuerNetId    = order.Issuer?.IssuerNetId ?? 0,
                    ReceiverNetId  = order.Receiver.NetworkObject != null ? order.Receiver.NetworkObject.NetworkObjectId : 0,
                    OrderTypeName  = order.OrderTypeName,
                    Priority       = (byte)order.Priority,
                    Urgency        = (byte)order.Urgency,
                    TimeoutSeconds = order.TimeoutSeconds,
                    ElapsedSeconds = 0f,
                });

                OnOrderReceived?.Invoke(order);
                StartCoroutine(EvaluateOrderRoutine(order));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private IEnumerator EvaluateOrderRoutine(Order order)
        {
            // NPC evaluation — server-side, blocking on _responseDelay then computing accept score
            // Player evaluation — Owner RPC popup; the player's response triggers ResolvePlayerOrderInternal
            if (_character == null || !_character.IsPlayer())
            {
                yield return new WaitForSeconds(_responseDelay);
                bool accepted = EvaluateNpcAcceptance(order);
                ApplyEvaluationResult(order, accepted);
                yield break;
            }

            // Player receiver path — fire the Owner RPC popup and wait
            ShowOrderPromptRpc(new PendingOrderSyncData
            {
                OrderId        = order.OrderId,
                IssuerNetId    = order.Issuer?.IssuerNetId ?? 0,
                ReceiverNetId  = order.Receiver.NetworkObject != null ? order.Receiver.NetworkObject.NetworkObjectId : 0,
                OrderTypeName  = order.OrderTypeName,
                Priority       = (byte)order.Priority,
                Urgency        = (byte)order.Urgency,
                TimeoutSeconds = order.TimeoutSeconds,
                ElapsedSeconds = 0f,
            });

            float waitElapsed = 0f;
            while (waitElapsed < order.TimeoutSeconds && order.State == OrderState.Pending)
            {
                waitElapsed += UnityEngine.Time.deltaTime;
                yield return null;
            }

            if (order.State == OrderState.Pending)
            {
                ApplyEvaluationResult(order, false);   // Player didn't respond — auto-refuse
            }
        }

        private bool EvaluateNpcAcceptance(Order order)
        {
            float score = 0.5f
                        + (order.Priority - 50f) / 100f;

            if (_character.CharacterRelation != null && order.Issuer?.AsCharacter != null)
            {
                if (_character.CharacterRelation.IsFriend(order.Issuer.AsCharacter)) score += 0.2f;
                else if (_character.CharacterRelation.IsEnemy(order.Issuer.AsCharacter)) score -= 0.4f;
            }

            if (_character.CharacterTraits != null)
            {
                score += (_character.CharacterTraits.GetLoyalty()      - 0.5f) * 0.3f;
                score -= (_character.CharacterTraits.GetAggressivity() - 0.5f) * 0.2f;
            }

            // Personality compatibility filter (matches CharacterRelation.UpdateRelation logic)
            if (_character.CharacterProfile != null && order.Issuer?.AsCharacter?.CharacterProfile != null)
            {
                int compat = _character.CharacterProfile.GetCompatibilityWith(order.Issuer.AsCharacter.CharacterProfile);
                if (compat > 0) score += 0.1f;
                else if (compat < 0) score -= 0.1f;
            }

            score = Mathf.Clamp01(score);
            bool accepted = UnityEngine.Random.value < score;
            Debug.Log($"<color=cyan>[Order]</color> {_character.CharacterName} evaluates {order.OrderTypeName} from {order.Issuer?.DisplayName ?? "anonymous"} (P={order.Priority}): score={score:F2} -> {(accepted ? "ACCEPTED" : "REFUSED")}");
            return accepted;
        }

        private void ApplyEvaluationResult(Order order, bool accepted)
        {
            try
            {
                RemovePendingSync(order.OrderId);

                if (!accepted)
                {
                    order.State = OrderState.Disobeyed;
                    FireConsequences(order);
                    OnOrderResolved?.Invoke(order, OrderState.Disobeyed);
                    _ordersByIdServer.Remove(order.OrderId);
                    return;
                }

                order.State = OrderState.Accepted;
                order.OnAccepted();
                order.State = OrderState.Active;
                _activeOrdersServer.Add(order);
                _activeOrdersSync.Add(BuildSyncData(order));
                OnOrderAccepted?.Invoke(order);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private OrderSyncData BuildSyncData(Order order)
        {
            var data = new OrderSyncData
            {
                OrderId              = order.OrderId,
                IssuerNetId          = order.Issuer?.IssuerNetId ?? 0,
                ReceiverNetId        = order.Receiver.NetworkObject != null ? order.Receiver.NetworkObject.NetworkObjectId : 0,
                OrderTypeName        = order.OrderTypeName,
                AuthorityContextName = order.AuthorityContext != null ? order.AuthorityContext.ContextName : "Stranger",
                Priority             = (byte)order.Priority,
                Urgency              = (byte)order.Urgency,
                State                = (byte)order.State,
                TimeoutSeconds       = order.TimeoutSeconds,
                ElapsedSeconds       = order.ElapsedSeconds,
                IsQuestBacked        = false,    // Set true once OrderQuest exists (Task 26)
                LinkedQuestId        = 0,        // Set when OrderQuest accepted (Task 26)
            };

            // Convert byte[] payload into FixedBytes62. Trim to 62 bytes; warn if oversized.
            byte[] payload = order.SerializeOrderPayload() ?? System.Array.Empty<byte>();
            if (payload.Length > 62)
            {
                Debug.LogWarning($"[Order] Payload for {order.OrderTypeName} is {payload.Length} bytes; truncating to 62. Use a ClientRpc for larger payloads.");
            }
            data.PayloadLength = (byte)Mathf.Min(payload.Length, 62);
            CopyBytesToFixed(payload, ref data.OrderPayload, data.PayloadLength);
            return data;
        }

        /// <summary>
        /// Copies up to <paramref name="length"/> bytes from <paramref name="src"/> into the
        /// FixedBytes62 fixed-layout struct. Uses named byte fields to avoid unsafe code and
        /// remain compatible with any asmdef configuration.
        /// </summary>
        private static void CopyBytesToFixed(byte[] src, ref FixedBytes62 dst, byte length)
        {
            if (src == null || length == 0) return;
            // Copy via the individual byte fields that FixedBytes62 exposes.
            // This mirrors the approach used in OrderSyncData.NetworkSerialize.
            int i = 0;
            if (i < length) dst.offset0000.byte0000 = src[i++]; if (i < length) dst.offset0000.byte0001 = src[i++];
            if (i < length) dst.offset0000.byte0002 = src[i++]; if (i < length) dst.offset0000.byte0003 = src[i++];
            if (i < length) dst.offset0000.byte0004 = src[i++]; if (i < length) dst.offset0000.byte0005 = src[i++];
            if (i < length) dst.offset0000.byte0006 = src[i++]; if (i < length) dst.offset0000.byte0007 = src[i++];
            if (i < length) dst.offset0000.byte0008 = src[i++]; if (i < length) dst.offset0000.byte0009 = src[i++];
            if (i < length) dst.offset0000.byte0010 = src[i++]; if (i < length) dst.offset0000.byte0011 = src[i++];
            if (i < length) dst.offset0000.byte0012 = src[i++]; if (i < length) dst.offset0000.byte0013 = src[i++];
            if (i < length) dst.offset0000.byte0014 = src[i++]; if (i < length) dst.offset0000.byte0015 = src[i++];
            if (i < length) dst.offset0016.byte0000 = src[i++]; if (i < length) dst.offset0016.byte0001 = src[i++];
            if (i < length) dst.offset0016.byte0002 = src[i++]; if (i < length) dst.offset0016.byte0003 = src[i++];
            if (i < length) dst.offset0016.byte0004 = src[i++]; if (i < length) dst.offset0016.byte0005 = src[i++];
            if (i < length) dst.offset0016.byte0006 = src[i++]; if (i < length) dst.offset0016.byte0007 = src[i++];
            if (i < length) dst.offset0016.byte0008 = src[i++]; if (i < length) dst.offset0016.byte0009 = src[i++];
            if (i < length) dst.offset0016.byte0010 = src[i++]; if (i < length) dst.offset0016.byte0011 = src[i++];
            if (i < length) dst.offset0016.byte0012 = src[i++]; if (i < length) dst.offset0016.byte0013 = src[i++];
            if (i < length) dst.offset0016.byte0014 = src[i++]; if (i < length) dst.offset0016.byte0015 = src[i++];
            if (i < length) dst.offset0032.byte0000 = src[i++]; if (i < length) dst.offset0032.byte0001 = src[i++];
            if (i < length) dst.offset0032.byte0002 = src[i++]; if (i < length) dst.offset0032.byte0003 = src[i++];
            if (i < length) dst.offset0032.byte0004 = src[i++]; if (i < length) dst.offset0032.byte0005 = src[i++];
            if (i < length) dst.offset0032.byte0006 = src[i++]; if (i < length) dst.offset0032.byte0007 = src[i++];
            if (i < length) dst.offset0032.byte0008 = src[i++]; if (i < length) dst.offset0032.byte0009 = src[i++];
            if (i < length) dst.offset0032.byte0010 = src[i++]; if (i < length) dst.offset0032.byte0011 = src[i++];
            if (i < length) dst.offset0032.byte0012 = src[i++]; if (i < length) dst.offset0032.byte0013 = src[i++];
            if (i < length) dst.offset0032.byte0014 = src[i++]; if (i < length) dst.offset0032.byte0015 = src[i++];
            if (i < length) dst.byte0048 = src[i++]; if (i < length) dst.byte0049 = src[i++];
            if (i < length) dst.byte0050 = src[i++]; if (i < length) dst.byte0051 = src[i++];
            if (i < length) dst.byte0052 = src[i++]; if (i < length) dst.byte0053 = src[i++];
            if (i < length) dst.byte0054 = src[i++]; if (i < length) dst.byte0055 = src[i++];
            if (i < length) dst.byte0056 = src[i++]; if (i < length) dst.byte0057 = src[i++];
            if (i < length) dst.byte0058 = src[i++]; if (i < length) dst.byte0059 = src[i++];
            if (i < length) dst.byte0060 = src[i++]; if (i < length) dst.byte0061 = src[i  ];
        }

        private void RemovePendingSync(ulong orderId)
        {
            for (int i = _pendingOrdersSync.Count - 1; i >= 0; i--)
            {
                if (_pendingOrdersSync[i].OrderId == orderId)
                {
                    _pendingOrdersSync.RemoveAt(i);
                    return;
                }
            }
        }

        private void RemoveActiveSync(ulong orderId)
        {
            for (int i = _activeOrdersSync.Count - 1; i >= 0; i--)
            {
                if (_activeOrdersSync[i].OrderId == orderId)
                {
                    _activeOrdersSync.RemoveAt(i);
                    return;
                }
            }
        }

        private void FireConsequences(Order order)
        {
            if (order.Consequences == null) return;
            foreach (var c in order.Consequences)
            {
                if (c == null) continue;
                try { c.Apply(order, order.Receiver, order.Issuer); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        private void FireRewards(Order order)
        {
            if (order.Rewards == null) return;
            foreach (var r in order.Rewards)
            {
                if (r == null) continue;
                try { r.Apply(order, order.Receiver, order.Issuer); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        // ── Server-side ticking ──────────────────────────────────────────
        private void Update()
        {
            if (!IsServer) return;
            float dt = UnityEngine.Time.deltaTime;
            _pollAccumulator += dt;
            bool poll = _pollAccumulator >= _compliancePollInterval;
            if (poll) _pollAccumulator = 0f;

            for (int i = _activeOrdersServer.Count - 1; i >= 0; i--)
            {
                var order = _activeOrdersServer[i];
                if (order == null) { _activeOrdersServer.RemoveAt(i); continue; }
                order.OnTick(dt);

                if (poll && order.IsComplied())
                {
                    ResolveActive(order, OrderState.Complied);
                    continue;
                }

                if (order.ElapsedSeconds >= order.TimeoutSeconds)
                {
                    ResolveActive(order, OrderState.Disobeyed);
                }
            }
        }

        private void ResolveActive(Order order, OrderState finalState)
        {
            try
            {
                _activeOrdersServer.Remove(order);
                RemoveActiveSync(order.OrderId);
                _ordersByIdServer.Remove(order.OrderId);

                order.State = finalState;
                order.OnResolved(finalState);

                if (finalState == OrderState.Complied) FireRewards(order);
                else if (finalState == OrderState.Disobeyed) FireConsequences(order);
                // Cancelled = neither

                OnOrderResolved?.Invoke(order, finalState);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ── Player resolution path (called from ResolvePlayerOrderServerRpc) ──
        internal void ResolvePlayerOrderInternal(ulong orderId, bool accept)
        {
            if (!IsServer) return;
            if (!_ordersByIdServer.TryGetValue(orderId, out var order)) return;
            if (order.State != OrderState.Pending) return;
            ApplyEvaluationResult(order, accept);
        }

        // ── Issuer cancellation ──────────────────────────────────────────
        public bool CancelIssuedOrder(ulong orderId)
        {
            if (!IsServer) return false;
            if (!_ordersByIdServer.TryGetValue(orderId, out var order)) return false;

            var receiverOrders = order.Receiver?.CharacterOrders;
            if (receiverOrders == null) return false;
            receiverOrders.ResolveActive(order, OrderState.Cancelled);
            _issuedOrdersServer.Remove(order);
            _ordersByIdServer.Remove(orderId);
            return true;
        }

        // ── GOAP accessor ────────────────────────────────────────────────
        public Order GetTopActiveOrder()
        {
            Order top = null;
            for (int i = 0; i < _activeOrdersServer.Count; i++)
            {
                var o = _activeOrdersServer[i];
                if (top == null || o.Priority > top.Priority) top = o;
            }
            return top;
        }

        // ── Player UI hooks (Owner-side) ─────────────────────────────────
        /// <summary>Server to Owning client: render the order prompt UI.</summary>
        [Rpc(SendTo.Owner)]
        public void ShowOrderPromptRpc(PendingOrderSyncData data)
        {
            // Client-side: invoke the UI layer. Wired by UI_OrderImmediatePopup in Task 23
            // (subscribed via OnOrderPromptShown event below).
            OnOrderPromptShown?.Invoke(data);
        }

        /// <summary>Client-side event the UI subscribes to. Fired on the owning client only.</summary>
        public event Action<PendingOrderSyncData> OnOrderPromptShown;

        /// <summary>Owning client to server: the player's accept/refuse response.</summary>
        [Rpc(SendTo.Server)]
        public void ResolvePlayerOrderServerRpc(ulong orderId, bool accept, RpcParams rpcParams = default)
        {
            // Authority check: only the receiver's owner can answer their own orders
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning($"[Order] ResolvePlayerOrderServerRpc rejected: sender {rpcParams.Receive.SenderClientId} != owner {OwnerClientId}.");
                return;
            }
            ResolvePlayerOrderInternal(orderId, accept);
        }

        /// <summary>
        /// Owning client to server: issue an order from this Character.
        /// <para>
        /// <paramref name="consequenceSoNamesPacked"/> and <paramref name="rewardSoNamesPacked"/>
        /// are pipe-delimited ('|') lists of SO asset names (e.g. "Consequence_RelationDrop|Consequence_StatusEffect").
        /// NGO cannot serialize <c>string[]</c> in RPCs, so we use <see cref="FixedString512Bytes"/> instead.
        /// Leave empty if no consequences/rewards are needed.
        /// </para>
        /// </summary>
        [Rpc(SendTo.Server)]
        public void IssueOrderServerRpc(
            ulong receiverNetId,
            FixedString64Bytes orderTypeName,
            byte urgency,
            byte[] orderPayload,
            FixedString512Bytes consequenceSoNamesPacked,
            FixedString512Bytes rewardSoNamesPacked,
            float timeoutSeconds,
            RpcParams rpcParams = default)
        {
            // Authority check
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning($"[Order] IssueOrderServerRpc rejected: sender {rpcParams.Receive.SenderClientId} != issuer owner {OwnerClientId}.");
                return;
            }

            try
            {
                if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(receiverNetId, out var receiverObj))
                {
                    Debug.LogError($"[Order] IssueOrderServerRpc: receiver NetId {receiverNetId} not found.");
                    return;
                }
                var receiver = receiverObj.GetComponent<Character>();
                if (receiver == null) return;

                var order = OrderFactory.Create(orderTypeName.ToString());
                if (order == null)
                {
                    Debug.LogError($"[Order] Unknown OrderTypeName: {orderTypeName}");
                    return;
                }

                order.OrderTypeName    = orderTypeName.ToString();
                order.Issuer           = _character;
                order.Receiver         = receiver;
                order.Urgency          = (OrderUrgency)urgency;
                order.AuthorityContext = AuthorityResolver.Resolve(_character, receiver);
                order.Priority         = Mathf.Clamp((order.AuthorityContext != null ? order.AuthorityContext.BasePriority : 20) + (int)order.Urgency, 0, 100);
                order.TimeoutSeconds   = timeoutSeconds;
                order.DeserializeOrderPayload(orderPayload);

                string consequencePacked = consequenceSoNamesPacked.ToString();
                if (!string.IsNullOrEmpty(consequencePacked))
                {
                    foreach (var n in consequencePacked.Split('|'))
                    {
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        var so = Resources.Load<ScriptableObject>($"Data/OrderConsequences/{n}");
                        if (so is IOrderConsequence c) order.Consequences.Add(c);
                    }
                }

                string rewardPacked = rewardSoNamesPacked.ToString();
                if (!string.IsNullOrEmpty(rewardPacked))
                {
                    foreach (var n in rewardPacked.Split('|'))
                    {
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        var so = Resources.Load<ScriptableObject>($"Data/OrderRewards/{n}");
                        if (so is IOrderReward r) order.Rewards.Add(r);
                    }
                }

                IssueOrder(order);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
