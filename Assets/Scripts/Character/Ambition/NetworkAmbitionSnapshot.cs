using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Compact, always-on replicated state of a Character's active ambition.
    /// Read by everyone (so other players' UI can show their visible ambitions in
    /// the dev inspector); written only by the server. The full DTO (with context,
    /// task states, history) is fanned out via ClientRpc on demand.
    /// </summary>
    public struct NetworkAmbitionSnapshot : INetworkSerializable, System.IEquatable<NetworkAmbitionSnapshot>
    {
        public bool HasActive;
        public FixedString64Bytes AmbitionSOGuid;
        public int CurrentStepIndex;
        public int TotalSteps;
        public float Progress01;
        public bool OverridesSchedule;

        public static NetworkAmbitionSnapshot Inactive => new()
        {
            HasActive = false,
            AmbitionSOGuid = default,
            CurrentStepIndex = 0,
            TotalSteps = 0,
            Progress01 = 0f,
            OverridesSchedule = false
        };

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref HasActive);
            s.SerializeValue(ref AmbitionSOGuid);
            s.SerializeValue(ref CurrentStepIndex);
            s.SerializeValue(ref TotalSteps);
            s.SerializeValue(ref Progress01);
            s.SerializeValue(ref OverridesSchedule);
        }

        public bool Equals(NetworkAmbitionSnapshot o)
            => HasActive == o.HasActive
            && AmbitionSOGuid.Equals(o.AmbitionSOGuid)
            && CurrentStepIndex == o.CurrentStepIndex
            && TotalSteps == o.TotalSteps
            && Mathf.Approximately(Progress01, o.Progress01)
            && OverridesSchedule == o.OverridesSchedule;
    }
}
