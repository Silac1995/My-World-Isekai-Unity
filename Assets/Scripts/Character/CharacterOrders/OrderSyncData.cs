// Assets/Scripts/Character/CharacterOrders/OrderSyncData.cs
using System;
using Unity.Collections;
using Unity.Netcode;

namespace MWI.Orders
{
    /// <summary>
    /// Network-friendly snapshot of an active Order. Single fixed schema; type-specific
    /// payload bytes live in OrderPayload to keep the NetworkList polymorphic-safe.
    ///
    /// NOTE: NetworkList<T> requires T to be an unmanaged struct, so byte[] is not
    /// allowed. The payload is stored as FixedBytes62 (max 62 bytes). Order subclasses
    /// must fit their serialized payload within this budget. Payloads larger than 62 bytes
    /// must fall back to a ClientRpc or a dedicated NetworkVariable on the receiver.
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
        /// <summary>
        /// Type-specific payload (target id, zone centre, etc.) packed as up to 62 bytes.
        /// Use <see cref="PayloadLength"/> to track the valid byte count; trailing bytes are zero-padded.
        /// </summary>
        public FixedBytes62       OrderPayload;
        public byte               PayloadLength;          // How many bytes in OrderPayload are valid (0–62)

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
            // FixedBytes62 has no INetworkSerializeByMemcpy, serialize byte-by-byte.
            // Only the first PayloadLength bytes carry data; we still serialize all 62
            // so the struct stays fixed-size on the wire (no length prefix needed).
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0000);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0001);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0002);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0003);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0004);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0005);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0006);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0007);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0008);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0009);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0010);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0011);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0012);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0013);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0014);
            serializer.SerializeValue(ref OrderPayload.offset0000.byte0015);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0000);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0001);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0002);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0003);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0004);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0005);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0006);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0007);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0008);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0009);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0010);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0011);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0012);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0013);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0014);
            serializer.SerializeValue(ref OrderPayload.offset0016.byte0015);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0000);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0001);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0002);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0003);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0004);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0005);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0006);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0007);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0008);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0009);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0010);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0011);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0012);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0013);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0014);
            serializer.SerializeValue(ref OrderPayload.offset0032.byte0015);
            serializer.SerializeValue(ref OrderPayload.byte0048);
            serializer.SerializeValue(ref OrderPayload.byte0049);
            serializer.SerializeValue(ref OrderPayload.byte0050);
            serializer.SerializeValue(ref OrderPayload.byte0051);
            serializer.SerializeValue(ref OrderPayload.byte0052);
            serializer.SerializeValue(ref OrderPayload.byte0053);
            serializer.SerializeValue(ref OrderPayload.byte0054);
            serializer.SerializeValue(ref OrderPayload.byte0055);
            serializer.SerializeValue(ref OrderPayload.byte0056);
            serializer.SerializeValue(ref OrderPayload.byte0057);
            serializer.SerializeValue(ref OrderPayload.byte0058);
            serializer.SerializeValue(ref OrderPayload.byte0059);
            serializer.SerializeValue(ref OrderPayload.byte0060);
            serializer.SerializeValue(ref OrderPayload.byte0061);
            serializer.SerializeValue(ref PayloadLength);
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
                && PayloadLength == other.PayloadLength
                && PayloadsEqual(ref OrderPayload, ref other.OrderPayload);
        }

        // FixedBytes62 and FixedBytes16 have no operator== or IEquatable overrides.
        // Compare all 62 bytes explicitly to avoid boxing allocations.
        private static bool PayloadsEqual(ref FixedBytes62 a, ref FixedBytes62 b)
        {
            return a.offset0000.byte0000 == b.offset0000.byte0000
                && a.offset0000.byte0001 == b.offset0000.byte0001
                && a.offset0000.byte0002 == b.offset0000.byte0002
                && a.offset0000.byte0003 == b.offset0000.byte0003
                && a.offset0000.byte0004 == b.offset0000.byte0004
                && a.offset0000.byte0005 == b.offset0000.byte0005
                && a.offset0000.byte0006 == b.offset0000.byte0006
                && a.offset0000.byte0007 == b.offset0000.byte0007
                && a.offset0000.byte0008 == b.offset0000.byte0008
                && a.offset0000.byte0009 == b.offset0000.byte0009
                && a.offset0000.byte0010 == b.offset0000.byte0010
                && a.offset0000.byte0011 == b.offset0000.byte0011
                && a.offset0000.byte0012 == b.offset0000.byte0012
                && a.offset0000.byte0013 == b.offset0000.byte0013
                && a.offset0000.byte0014 == b.offset0000.byte0014
                && a.offset0000.byte0015 == b.offset0000.byte0015
                && a.offset0016.byte0000 == b.offset0016.byte0000
                && a.offset0016.byte0001 == b.offset0016.byte0001
                && a.offset0016.byte0002 == b.offset0016.byte0002
                && a.offset0016.byte0003 == b.offset0016.byte0003
                && a.offset0016.byte0004 == b.offset0016.byte0004
                && a.offset0016.byte0005 == b.offset0016.byte0005
                && a.offset0016.byte0006 == b.offset0016.byte0006
                && a.offset0016.byte0007 == b.offset0016.byte0007
                && a.offset0016.byte0008 == b.offset0016.byte0008
                && a.offset0016.byte0009 == b.offset0016.byte0009
                && a.offset0016.byte0010 == b.offset0016.byte0010
                && a.offset0016.byte0011 == b.offset0016.byte0011
                && a.offset0016.byte0012 == b.offset0016.byte0012
                && a.offset0016.byte0013 == b.offset0016.byte0013
                && a.offset0016.byte0014 == b.offset0016.byte0014
                && a.offset0016.byte0015 == b.offset0016.byte0015
                && a.offset0032.byte0000 == b.offset0032.byte0000
                && a.offset0032.byte0001 == b.offset0032.byte0001
                && a.offset0032.byte0002 == b.offset0032.byte0002
                && a.offset0032.byte0003 == b.offset0032.byte0003
                && a.offset0032.byte0004 == b.offset0032.byte0004
                && a.offset0032.byte0005 == b.offset0032.byte0005
                && a.offset0032.byte0006 == b.offset0032.byte0006
                && a.offset0032.byte0007 == b.offset0032.byte0007
                && a.offset0032.byte0008 == b.offset0032.byte0008
                && a.offset0032.byte0009 == b.offset0032.byte0009
                && a.offset0032.byte0010 == b.offset0032.byte0010
                && a.offset0032.byte0011 == b.offset0032.byte0011
                && a.offset0032.byte0012 == b.offset0032.byte0012
                && a.offset0032.byte0013 == b.offset0032.byte0013
                && a.offset0032.byte0014 == b.offset0032.byte0014
                && a.offset0032.byte0015 == b.offset0032.byte0015
                && a.byte0048 == b.byte0048
                && a.byte0049 == b.byte0049
                && a.byte0050 == b.byte0050
                && a.byte0051 == b.byte0051
                && a.byte0052 == b.byte0052
                && a.byte0053 == b.byte0053
                && a.byte0054 == b.byte0054
                && a.byte0055 == b.byte0055
                && a.byte0056 == b.byte0056
                && a.byte0057 == b.byte0057
                && a.byte0058 == b.byte0058
                && a.byte0059 == b.byte0059
                && a.byte0060 == b.byte0060
                && a.byte0061 == b.byte0061;
        }
    }
}
