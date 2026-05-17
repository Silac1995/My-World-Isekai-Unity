using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Project-wide convention helper for "which commercial building does this NPC
/// decide to buy from?". Replaces deterministic first-found shop selection with
/// a reputation-weighted random pick, so higher-rep buildings are visited more
/// often without any single shop ever being permanently invisible to the AI.
///
/// <para>
/// <b>Formula (2026-05-17g — pool-relative floor for unbounded rep):</b>
/// <code>floor = max(<see cref="AbsoluteFloor"/>, maxRepInPool / <see cref="RelativeFloorDivisor"/>)</code>
/// <code>weight = max(floor, building.Reputation)</code>
/// The relative floor — 1/10 of the pool's max rep — guarantees the lowest-rep
/// candidate keeps a <b>10% minimum relative weight</b> vs the highest-rep
/// candidate <i>regardless of absolute scale</i>. So with pool max-rep = 100
/// the lowest gets weight ≥10 (= old behaviour); with pool max-rep = 1000
/// the lowest gets weight ≥100, still 10% of the leader. Without this,
/// reputation outliers (rep 1000 vs rep 50) would dwarf the absolute-10 floor
/// and make low-rep buildings effectively invisible.
/// </para>
///
/// <para>
/// <see cref="AbsoluteFloor"/> (= 10) is the safety floor when the entire pool
/// is at rep 0 — keeps the picker from dividing by zero and gives a uniform
/// 1-in-N chance for an all-zero pool. The pool-relative floor only takes
/// effect once at least one candidate has positive rep.
/// </para>
///
/// <para>
/// "10% floor" is a <i>weight ratio</i> (10% of the leader's weight), not a
/// raw probability — raw % for the lowest dilutes naturally as more
/// candidates join the pool (with N candidates the lowest's raw chance is
/// floor / (sum-of-weights)).
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
/// (see <c>wiki/systems/commercial-treasury.md §Reputation</c>). Refactored
/// 2026-05-17g to a pool-relative floor so the 10% invariant survives the
/// removal of the rep upper bound.
/// </summary>
public static class ReputationWeightedPicker
{
    /// <summary>
    /// Absolute safety floor. Used only when the pool's max reputation is 0
    /// (all candidates at rock-bottom) so the picker still has a non-zero
    /// total weight to roll against. Above that, the pool-relative floor takes
    /// over and scales with the leader.
    /// </summary>
    public const int AbsoluteFloor = 10;

    /// <summary>
    /// Divisor that yields the pool-relative floor:
    /// <c>floor = maxRepInPool / RelativeFloorDivisor</c>. Default 10 → lowest
    /// candidate keeps at least 10% of the leader's weight.
    /// </summary>
    public const int RelativeFloorDivisor = 10;

    /// <summary>
    /// Weighted-random pick across <paramref name="candidates"/>. Returns the
    /// single matched <c>T</c>, or <c>null</c> when the input is null/empty.
    /// Single-candidate fast-path returns that candidate without rolling.
    /// </summary>
    public static T Pick<T>(IList<T> candidates) where T : CommercialBuilding
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        // Pass 1: find pool max → compute the effective floor.
        int maxRep = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null) continue;
            if (c.Reputation > maxRep) maxRep = c.Reputation;
        }
        int floor = Mathf.Max(AbsoluteFloor, maxRep / RelativeFloorDivisor);

        // Pass 2: sum weights.
        int totalWeight = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null) continue;
            totalWeight += Mathf.Max(floor, c.Reputation);
        }
        if (totalWeight <= 0) return candidates[0]; // defensive — unreachable with the AbsoluteFloor.

        // Pass 3: roll + accumulate.
        int roll = UnityEngine.Random.Range(0, totalWeight); // [0, totalWeight)
        int accum = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null) continue;
            accum += Mathf.Max(floor, c.Reputation);
            if (roll < accum) return c;
        }
        // Null-skip safety net (Random.Range upper bound is exclusive, so unreachable
        // in practice unless every candidate is null — guarded above).
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i] != null) return candidates[i];
        }
        return null;
    }
}
