using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Continuous, condition-terminated, cancel-on-movement action. Per server tick:
///   1. Compute consume budget = 1 + actor.GetSkillLevelOrZero(SkillId.Builder) / N
///   2. For each pending requirement, consume up to budget units from
///      Building.BuildingZone (despawn matching WorldItems) and the actor's inventory.
///   3. Recompute progress; write Building.ConstructionProgress + DeliveredMaterials.
///   4. If progress >= 1f → Building.Finalize() and return true (action ends).
///   5. If consumed nothing this tick → stallTicks++; return true once stall limit hit.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
public class CharacterAction_FinishConstruction : CharacterAction_Continuous
{
    /// <summary>Skill formula denominator (Phase 1 unused — actor.GetSkillLevelOrZero == 0).</summary>
    public const int SkillBudgetDivisor = 5;

    /// <summary>Auto-exit after this many no-consume ticks (~5s at 1 Hz default).</summary>
    public const int MaxStallTicks = 5;

    private readonly Building _target;
    private int _stallTicks;
    private readonly List<WorldItem> _scratch = new List<WorldItem>(32);

    public override string ActionName => "Finish Construction";

    /// <summary>
    /// HUD progress bar reads this. Mirrors Building.ConstructionProgress, which is
    /// updated server-side every OnTick by this action AND by ConstructionSiteScanner
    /// (between actions). Replicated to all peers via NetworkVariable so clients see
    /// the same fill level the server computes.
    /// </summary>
    public override float Progress =>
        _target != null ? _target.ConstructionProgress.Value : 0f;

    public CharacterAction_FinishConstruction(Character character, Building target) : base(character)
    {
        _target = target;
        TickIntervalSeconds = 1f;
    }

    public override bool CanExecute()
    {
        if (_target == null || character == null) return false;
        if (!_target.IsUnderConstruction) return false;
        // Cooperative model: any character can finalize (owner-check removed 2026-05-06).
        // Spatial gate stays — Core Rule #1.
        if (!IsActorInsideBuildingZone()) return false;
        return true;
    }

    public override void OnStart()
    {
        _stallTicks = 0;
    }

    public override bool OnTick()
    {
        if (_target == null || character == null) return true;

        // Re-validate every tick (state + position). Owner-check removed 2026-05-06 —
        // any character in the zone can keep ticking the action.
        if (!_target.IsUnderConstruction) return true;
        if (!IsActorInsideBuildingZone()) return true;

        int budget = 1 + (character.GetSkillLevelOrZero(SkillId.Builder) / SkillBudgetDivisor);
        int totalConsumed = 0;

        var reqs = _target.ConstructionRequirements;
        if (reqs == null || reqs.Count == 0) return true;

        for (int i = 0; i < reqs.Count && budget > 0; i++)
        {
            var req = reqs[i];
            // CraftingIngredient is a struct; only the Item field can be null.
            if (req.Item == null) continue;

            int currentDelivered = _target.ContributedMaterials.TryGetValue(req.Item, out int v) ? v : 0;
            int needed = req.Amount - currentDelivered;
            if (needed <= 0) continue;

            int take = Mathf.Min(needed, budget);

            int fromZone = ConsumeFromZone(_target.BuildingZone, req.Item, take);
            int fromInv = ConsumeFromActorInventory(character, req.Item, take - fromZone);
            int consumed = fromZone + fromInv;

            if (consumed > 0)
            {
                _target.ContributeMaterial(req.Item, consumed); // existing method bumps _contributedMaterials
            }

            totalConsumed += consumed;
            budget -= consumed;
        }

        // Update networked meter.
        float progress = _target.ComputeProgress();
        if (Mathf.Abs(progress - _target.ConstructionProgress.Value) > 0.001f)
        {
            _target.ConstructionProgress.Value = Mathf.Clamp01(progress);
        }

        if (progress >= 1f)
        {
            _target.Finalize();
            return true; // done
        }

        if (totalConsumed == 0)
        {
            _stallTicks++;
            if (_stallTicks >= MaxStallTicks) return true; // graceful exit
        }
        else
        {
            _stallTicks = 0;
        }

        return false;
    }

    public override void OnCancel()
    {
        // No rollback — already-consumed credits stay locked. Owner can re-engage.
        _stallTicks = 0;
    }

    // ────────────────────── Helpers ──────────────────────

    private bool IsActorInsideBuildingZone()
    {
        if (_target == null) { UnityEngine.Debug.LogWarning("[FinishConstruction.IsActorInside] _target null"); return false; }
        if (_target.BuildingZone == null) { UnityEngine.Debug.LogWarning($"[FinishConstruction.IsActorInside] {_target.BuildingName} _target.BuildingZone null"); return false; }
        var bz = _target.BuildingZone;
        var bounds = bz.bounds;
        var pos = character.transform.position;
        bool contains = bounds.Contains(pos);
        var min = bounds.min;
        var max = bounds.max;
        bool xOk = pos.x >= min.x && pos.x <= max.x;
        bool yOk = pos.y >= min.y && pos.y <= max.y;
        bool zOk = pos.z >= min.z && pos.z <= max.z;
        // 2D footprint check — ignores Y because the construction zone is conceptually
        // a ground rectangle. The character's exact Y (which can be 0 ± floating-point
        // noise depending on collider/NavMesh agent position) shouldn't gate interaction
        // when they're standing inside the X-Z footprint.
        bool xzInside = xOk && zOk;
        UnityEngine.Debug.Log($"[FinishConstruction.IsActorInside] {_target.BuildingName} pos={pos} min={min} max={max} | x:{xOk}({pos.x:F4} in [{min.x:F4},{max.x:F4}]) y:{yOk}({pos.y:F4} in [{min.y:F4},{max.y:F4}]) z:{zOk}({pos.z:F4} in [{min.z:F4},{max.z:F4}]) | bounds.Contains={contains} | xzInside={xzInside}");
        return xzInside;
    }

    /// <summary>
    /// Despawns up to `amount` WorldItems whose ItemSO matches `target` from the zone.
    /// Each WorldItem is 1 unit (project does NOT use stack-based items). Returns the
    /// actual number of WorldItems despawned. Server-only API.
    /// </summary>
    private int ConsumeFromZone(Collider zoneCollider, ItemSO target, int amount)
    {
        if (amount <= 0 || zoneCollider == null || target == null) return 0;

        _target.GetPhysicalItemsInCollider(zoneCollider, _scratch);

        int consumed = 0;
        for (int i = 0; i < _scratch.Count && consumed < amount; i++)
        {
            var w = _scratch[i];
            if (w == null || w.IsBeingCarried) continue;
            // Each WorldItem represents 1 unit; project is non-stacking.
            // Access ItemSO via ItemInstance per project convention (see ConstructionSiteScanner).
            var instance = w.ItemInstance;
            if (instance == null) continue;
            if (instance.ItemSO != target) continue;

            try
            {
                var no = w.GetComponent<Unity.Netcode.NetworkObject>();
                if (no != null && no.IsSpawned) no.Despawn(destroy: true);
                else GameObject.Destroy(w.gameObject);
                consumed++;
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        return consumed;
    }

    /// <summary>
    /// Server-only. Bonus path for owners who carry construction items in inventory.
    /// Phase 1: stub returns 0 (does not touch CharacterEquipment). Wire the real
    /// inventory pull in a follow-up after PlayMode-MP confirms the zone path works
    /// end-to-end.
    /// </summary>
    private int ConsumeFromActorInventory(Character actor, ItemSO target, int amount)
    {
        // Phase 1 stub.
        return 0;
    }
}
