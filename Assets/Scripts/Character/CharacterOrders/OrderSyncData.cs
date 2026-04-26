// Assets/Scripts/Character/CharacterOrders/OrderSyncData.cs
using System;
using Unity.Collections;
using Unity.Netcode;

namespace MWI.Orders
{
    /// <summary>
    /// Network-friendly snapshot of an active Order. Single fixed schema; type-specific
    /// payload bytes live in OrderPayload to keep the NetworkList polymorphic-safe.
    /// </summary>
    [Serializable]
    public struct OrderSyncData : INetworkSerializable, IEquatable<OrderSyncData>
    {
        public ulong              OrderId;
        public ulong              IssuerNetId;            // 0 = anonymous
        public ulong              ReceiverNetId;
        public FixedString64Bytes OrderTypeName;          // e.g., "Order_Kill"
        public FixedString32Bytes AuthorityContextName;   // e.g., "Captain"
        public byte               Priority;
        public byte               Urgency;
        public byte               State;
        public float              TimeoutSeconds;
        public float              ElapsedSeconds;
        public bool               IsQuestBacked;
        public ulong              LinkedQuestId;
        public byte[]             OrderPayload;           // Type-specific (target id, zone center, etc.)

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OrderId);
            serializer.SerializeValue(ref IssuerNetId);
            serializer.SerializeValue(ref ReceiverNetId);
            serializer.SerializeValue(ref OrderTypeName);
            serializer.SerializeValue(ref AuthorityContextName);
            serializer.SerializeValue(ref Priority);
            serializer.SerializeValue(ref Urgency);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref TimeoutSeconds);
            serializer.SerializeValue(ref ElapsedSeconds);
            serializer.SerializeValue(ref IsQuestBacked);
            serializer.SerializeValue(ref LinkedQuestId);

            // byte[] payload — write length then bytes
            int length = OrderPayload?.Length ?? 0;
            serializer.SerializeValue(ref length);
            if (serializer.IsReader && length > 0)
            {
                OrderPayload = new byte[length];
            }
            for (int i = 0; i < length; i++)
            {
                byte b = OrderPayload[i];
                serializer.SerializeValue(ref b);
                if (serializer.IsReader) OrderPayload[i] = b;
            }
        }

        public bool Equals(OrderSyncData other)
        {
            return OrderId == other.OrderId
                && IssuerNetId == other.IssuerNetId
                && ReceiverNetId == other.ReceiverNetId
                && OrderTypeName.Equals(other.OrderTypeName)
                && AuthorityContextName.Equals(other.AuthorityContextName)
                && Priority == other.Priority
                && Urgency == other.Urgency
                && State == other.State
                && TimeoutSeconds == other.TimeoutSeconds
                && ElapsedSeconds == other.ElapsedSeconds
                && IsQuestBacked == other.IsQuestBacked
                && LinkedQuestId == other.LinkedQuestId
                && PayloadsEqual(OrderPayload, other.OrderPayload);
        }

        private static bool PayloadsEqual(byte[] a, byte[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length)   return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
