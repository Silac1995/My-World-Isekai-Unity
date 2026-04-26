// Assets/Scripts/Character/CharacterOrders/PendingOrderSyncData.cs
using System;
using Unity.Collections;
using Unity.Netcode;

namespace MWI.Orders
{
    /// <summary>
    /// Slimmer snapshot for in-evaluation orders. No payload, no LinkedQuestId — just
    /// enough to drive the player popup countdown.
    /// </summary>
    [Serializable]
    public struct PendingOrderSyncData : INetworkSerializable, IEquatable<PendingOrderSyncData>
    {
        public ulong              OrderId;
        public ulong              IssuerNetId;
        public ulong              ReceiverNetId;
        public FixedString64Bytes OrderTypeName;
        public byte               Priority;
        public byte               Urgency;
        public float              TimeoutSeconds;
        public float              ElapsedSeconds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OrderId);
            serializer.SerializeValue(ref IssuerNetId);
            serializer.SerializeValue(ref ReceiverNetId);
            serializer.SerializeValue(ref OrderTypeName);
            serializer.SerializeValue(ref Priority);
            serializer.SerializeValue(ref Urgency);
            serializer.SerializeValue(ref TimeoutSeconds);
            serializer.SerializeValue(ref ElapsedSeconds);
        }

        public bool Equals(PendingOrderSyncData other)
        {
            return OrderId == other.OrderId
                && IssuerNetId == other.IssuerNetId
                && ReceiverNetId == other.ReceiverNetId
                && OrderTypeName.Equals(other.OrderTypeName)
                && Priority == other.Priority
                && Urgency == other.Urgency
                && TimeoutSeconds == other.TimeoutSeconds
                && ElapsedSeconds == other.ElapsedSeconds;
        }
    }
}
