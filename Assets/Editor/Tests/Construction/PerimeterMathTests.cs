using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pins the NearestPerimeterPoint geometry used by Building.EvictLeftoversToPerimeter
/// (Task 9). Tests the AABB projection in isolation against the same Bounds + Vector3
/// shape Unity returns from BoxCollider.bounds. Y is preserved (vertical faces only —
/// items eject sideways, never up/down through the floor).
/// </summary>
public class PerimeterMathTests
{
    private static (Vector3 point, Vector3 normal) Nearest(Bounds bounds, Vector3 inside)
    {
        float dxMin = inside.x - bounds.min.x;
        float dxMax = bounds.max.x - inside.x;
        float dzMin = inside.z - bounds.min.z;
        float dzMax = bounds.max.z - inside.z;

        float minDist = dxMin;
        Vector3 normal = Vector3.left;
        Vector3 face = new Vector3(bounds.min.x, inside.y, inside.z);

        if (dxMax < minDist) { minDist = dxMax; normal = Vector3.right;   face = new Vector3(bounds.max.x, inside.y, inside.z); }
        if (dzMin < minDist) { minDist = dzMin; normal = Vector3.back;    face = new Vector3(inside.x, inside.y, bounds.min.z); }
        if (dzMax < minDist) {                  normal = Vector3.forward; face = new Vector3(inside.x, inside.y, bounds.max.z); }

        return (face, normal);
    }

    private static Bounds Box(float minX, float minZ, float maxX, float maxZ, float y = 0f)
        => new Bounds(
            center: new Vector3((minX + maxX) * 0.5f, y, (minZ + maxZ) * 0.5f),
            size:   new Vector3(maxX - minX, 1f, maxZ - minZ));

    [Test] public void Centre_NearestIsAnyFace_ButValid()
    {
        // 10x10 box centred at origin, item at centre — any face is equidistant; we just
        // assert the result lies on the box surface.
        var b = Box(-5f, -5f, 5f, 5f);
        var r = Nearest(b, Vector3.zero);
        Assert.IsTrue(Mathf.Abs(r.point.x) == 5f || Mathf.Abs(r.point.z) == 5f);
    }

    [Test] public void OffCentreEastward_PicksEastFace()
    {
        var b = Box(-5f, -5f, 5f, 5f);
        var r = Nearest(b, new Vector3(3f, 0f, 0f));
        Assert.AreEqual(5f, r.point.x);
        Assert.AreEqual(Vector3.right, r.normal);
    }

    [Test] public void OffCentreNorthward_PicksNorthFace()
    {
        var b = Box(-5f, -5f, 5f, 5f);
        var r = Nearest(b, new Vector3(0f, 0f, 4f));
        Assert.AreEqual(5f, r.point.z);
        Assert.AreEqual(Vector3.forward, r.normal);
    }

    [Test] public void NonOriginBox_PicksCorrectFace()
    {
        var b = Box(10f, 10f, 20f, 20f);
        var r = Nearest(b, new Vector3(11f, 0f, 15f));
        Assert.AreEqual(10f, r.point.x);
        Assert.AreEqual(Vector3.left, r.normal);
    }

    [Test] public void YCoordinate_IsPreserved()
    {
        var b = Box(-5f, -5f, 5f, 5f, y: 2.5f);
        var r = Nearest(b, new Vector3(3f, 7.3f, 0f));
        Assert.AreEqual(7.3f, r.point.y, 0.0001f);
    }
}
