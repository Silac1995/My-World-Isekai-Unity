using Unity.Collections;
using Unity.Netcode;

namespace MWI.Ambition
{
    /// <summary>
    /// Per-entry wire shape for CharacterAmbition.RequestHistoryServerRpc.
    /// Bigger than the always-on Snapshot and changes rarely, so we don't replicate
    /// the whole History list — clients explicitly request it (dev inspector,
    /// dialogue trigger) and the server replies via DeliverHistoryClientRpc.
    /// </summary>
    public struct HistoryEntryNet : INetworkSerializable
    {
        public FixedString64Bytes AmbitionSOGuid;
        public int CompletedDay;
        public int Reason; // CompletionReason cast to int

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref AmbitionSOGuid);
            s.SerializeValue(ref CompletedDay);
            s.SerializeValue(ref Reason);
        }
    }
}
