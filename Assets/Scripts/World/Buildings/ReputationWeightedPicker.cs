using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Project-wide convention helper for "which commercial building does this NPC
/// decide to buy from?". Replaces deterministic first-found shop selection with
/// a reputation-weighted random pick, so higher-rep buildings are visited more
/// often without any single shop ever being permanently invisible to the AI.
///
/// <para>
/// <b>Formula:</b> <c>weight = max(<see cref="WeightFloor"/>, building.Reputation)</c>.
/// The floor (10) vs the rep ceiling (<see cref="CommercialBuilding.ReputationMax"/> = 100)
/// guarantees the lowest-rep building always has a relative weight of
/// 10/100 = 10% of a top-rep building. "10% floor" is a <i>weight ratio</i>,
/// not a raw probability — raw % for the lowest dilutes naturally as more
/// candidates join the pool (with N candidates the lowest's raw chance is
/// 10 / (sum-of-weights)).
/// </para>
///
/// <para>
/// <b>Use everywhere an NPC picks a commercial building to buy / use a service
/// from</b> — customer-NPC shop visits (<c>GoapAction_GoShopping</c>), B2B
/// procurement (<c>LogisticsStockEvaluator.TryB2BPurchaseFromShop</c>), future
/// banker / hire-help / commission-craft flows. Caller is responsible for the
/// qualifier filter (catalog match, stock present, gates passed, rep ≥ subtype
/// minimum where applicable); this helper only picks weighted among already-
/// qualifying candidates.
/// </para>
///
/// <para>
/// <b>Server-only.</b> Uses <see cref="UnityEngine.Random"/> which is shared
/// per-peer state. All NPC decision logic runs server-authoritatively, so the
/// pick is deterministic per server.
/// </para>
///
/// Authored 2026-05-17d as the canonical implementation behind the
/// "NPC-buys-from-building → reputation-weighted decision" convention
/// (see <c>wiki/systems/commercial-treasury.md §Reputation</c>).
/// </summary>
public static class ReputationWeightedPicker
{
    /// <summary>
    /// Minimum weight for any candidate. Guarantees the lowest-rep building keeps
    /// a 10:<see cref="CommercialBuilding.ReputationMax"/> = 10% relative weight
    /// vs a top-rep building.
    /// </summary>
    public const int WeightFloor = 10;

    /// <summary>
    /// Weighted-random pick across <paramref name="candidates"/>. Returns the
    /// single matched <c>T</c>, or <c>null</c> when the input is null/empty.
    /// Single-candidate fast-path returns that candidate without rolling.
    /// </summary>
    public static T Pick<T>(IList<T> candidates) where T : CommercialBuilding
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        int totalWeight = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null) continue;
            totalWeight += Mathf.Max(WeightFloor, c.Reputation);
        }
        if (totalWeight <= 0) return candidates[0]; // defensive — unreachable with the floor.

        int roll = UnityEngine.Random.Range(0, totalWeight); // [0, totalWeight)
        int accum = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null) continue;
            accum += Mathf.Max(WeightFloor, c.Reputation);
            if (roll < accum) return c;
        }
        // Floating-point / null-skip safety net (Random.Range upper bound is exclusive,
        // so unreachable in practice unless every candidate is null — guarded above).
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i] != null) return candidates[i];
        }
        return null;
    }
}
