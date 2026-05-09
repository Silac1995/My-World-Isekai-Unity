using System;
using Unity.Netcode;

/// <summary>
/// One delivered-material slot replicated through Building.DeliveredMaterials NetworkList.
/// RequirementIndex is the position in Building._constructionRequirements (compact —
/// avoids replicating ItemSO refs/strings every change).
///
/// Delivered is plain data — the owner (Building / ConstructionSiteScanner) is responsible
/// for clamping it to the requirement's needed quantity before mutating the NetworkList.
/// The struct itself never validates.
/// </summary>
[Serializable]
public struct DeliveredMaterialEntry : INetworkSerializable, IEquatable<DeliveredMaterialEntry>
{
    public int RequirementIndex;
    public int Delivered;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref RequirementIndex);
        serializer.SerializeValue(ref Delivered);
    }

    public bool Equals(DeliveredMaterialEntry other)
        => RequirementIndex == other.RequirementIndex && Delivered == other.Delivered;

    public override bool Equals(object obj)
        => obj is DeliveredMaterialEntry e && Equals(e);

    public override int GetHashCode()
        => unchecked((RequirementIndex * 397) ^ Delivered);
}
