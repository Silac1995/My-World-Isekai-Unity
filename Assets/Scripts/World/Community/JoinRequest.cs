using Unity.Netcode;

/// <summary>
/// Server-replicated record of a drifter's request to join a chartered community via the
/// AB's JoinRequestDesk. Lives on <see cref="AdministrativeBuilding.PendingJoinRequests"/>
/// (NetworkList). Standard NGO replication handles late-joiners + per-element add/remove.
///
/// Equality is keyed on <see cref="ApplicantNetId"/> so dedup-on-resubmit + remove-by-applicant
/// work without per-element scan helpers.
///
/// Plan 4c Task 6.
/// </summary>
public struct JoinRequest : INetworkSerializable, System.IEquatable<JoinRequest>
{
    /// <summary>NetworkObject ID of the applicant Character at request time.</summary>
    public ulong ApplicantNetId;

    /// <summary>Simulation day the request was submitted, for timeout / display.</summary>
    public int RequestedAtDay;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ApplicantNetId);
        serializer.SerializeValue(ref RequestedAtDay);
    }

    public bool Equals(JoinRequest other) => ApplicantNetId == other.ApplicantNetId;

    public override bool Equals(object obj) => obj is JoinRequest jr && Equals(jr);

    public override int GetHashCode() => ApplicantNetId.GetHashCode();
}
