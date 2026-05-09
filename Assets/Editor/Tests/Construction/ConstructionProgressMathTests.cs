using NUnit.Framework;
using System.Collections.Generic;

/// <summary>
/// Pins the construction progress formula in isolation so it can be refactored
/// inside Building.ComputeProgress (Task 9) without re-running PlayMode tests.
/// Mirrors progress = clamped sum(min(deliveredᵢ, requiredᵢ)) / sum(requiredᵢ).
/// </summary>
public class ConstructionProgressMathTests
{
    private static float Compute(int[] required, int[] delivered)
    {
        int totalRequired = 0;
        int totalSatisfied = 0;
        for (int i = 0; i < required.Length; i++)
        {
            int r = required[i];
            int d = i < delivered.Length ? delivered[i] : 0;
            totalRequired += r;
            totalSatisfied += System.Math.Min(d, r);
        }
        if (totalRequired <= 0) return 1f; // empty requirements → already complete
        return UnityEngine.Mathf.Clamp01((float)totalSatisfied / totalRequired);
    }

    [Test] public void EmptyRequirements_ReturnsOne()
        => Assert.AreEqual(1f, Compute(new int[0], new int[0]));

    [Test] public void NothingDelivered_ReturnsZero()
        => Assert.AreEqual(0f, Compute(new[] { 100 }, new[] { 0 }));

    [Test] public void HalfDelivered_ReturnsHalf()
        => Assert.AreEqual(0.5f, Compute(new[] { 100 }, new[] { 50 }));

    [Test] public void OverDelivered_ClampsToOne()
        => Assert.AreEqual(1f, Compute(new[] { 100 }, new[] { 200 }));

    [Test] public void MultiType_PartialAcrossTypes()
    {
        // 50/100 logs (0.5) + 30/100 stones (0.3) → satisfied=80, required=200 → 0.4
        Assert.AreEqual(0.4f, Compute(new[] { 100, 100 }, new[] { 50, 30 }));
    }

    [Test] public void MultiType_OneOverdeliveredOneEmpty()
    {
        // 200/100 logs (clamped to 100) + 0/50 stones → 100/150 = 0.6666…
        var p = Compute(new[] { 100, 50 }, new[] { 200, 0 });
        Assert.AreEqual(100f / 150f, p, 0.0001f);
    }
}
