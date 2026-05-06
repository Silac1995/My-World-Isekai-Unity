using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

public class DeliveredMaterialEntrySerializationTests
{
    [Test]
    public void RoundTrips_RequirementIndex_AndDelivered()
    {
        var original = new DeliveredMaterialEntry
        {
            RequirementIndex = 3,
            Delivered = 17
        };

        // Use FastBufferWriter / FastBufferReader (NGO's primitive serialization API).
        // BufferSerializer<T>'s constructor is internal to Unity.Netcode, so external
        // assemblies must drive the round-trip via the public WriteNetworkSerializable
        // / ReadNetworkSerializable extension points (which themselves invoke the
        // struct's NetworkSerialize override under the hood).
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteNetworkSerializable(original);

        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out DeliveredMaterialEntry roundtripped);

        Assert.AreEqual(original.RequirementIndex, roundtripped.RequirementIndex);
        Assert.AreEqual(original.Delivered, roundtripped.Delivered);
    }

    [Test]
    public void Equality_BasedOnFields()
    {
        var a = new DeliveredMaterialEntry { RequirementIndex = 1, Delivered = 5 };
        var b = new DeliveredMaterialEntry { RequirementIndex = 1, Delivered = 5 };
        var c = new DeliveredMaterialEntry { RequirementIndex = 1, Delivered = 6 };

        Assert.IsTrue(a.Equals(b));
        Assert.IsFalse(a.Equals(c));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }
}
