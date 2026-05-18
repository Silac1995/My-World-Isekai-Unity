using System;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Ambition
{
    /// <summary>
    /// Passive ambition task: completes once the actor's community has reached the
    /// target tier (by <see cref="CommunityTierRequirementsSO.Order"/>). Does NOT drive
    /// the promotion — <see cref="Community.TryPromoteLevel"/> owns that.
    ///
    /// <para>Three targeting modes, in priority order:
    /// <list type="number">
    /// <item><see cref="TargetTier"/> — direct SO reference (preferred for new content).</item>
    /// <item><see cref="TargetTierId"/> — stable string id resolved via <see cref="CommunityTierRegistry.GetById"/>.</item>
    /// <item><see cref="TargetLevel"/> — legacy <see cref="CommunityLevel"/> enum, kept for
    /// the 7 pre-existing Ambition_FoundACity quest assets. Resolved via the registry.</item>
    /// </list>
    /// Completion compares by <see cref="CommunityTierRequirementsSO.Order"/> so a custom
    /// designer-authored tier with Order=2.5 between Camp and Village sequences correctly.</para>
    /// </summary>
    [Serializable]
    public class Task_PromoteCommunity : TaskBase
    {
        [Header("Target tier (priority: TargetTier > TargetTierId > TargetLevel)")]
        [Tooltip("Authoritative SO reference. Leave null to fall back to TargetTierId / TargetLevel.")]
        public CommunityTierRequirementsSO TargetTier;

        [Tooltip("Stable string id (e.g. \"TierRequirements_Camp\"). Used when TargetTier is null. Empty falls back to TargetLevel.")]
        public string TargetTierId = string.Empty;

        /// <summary>Legacy enum-based target. Resolved via CommunityTierRegistry when the SO ref + id are empty.</summary>
        public CommunityLevel TargetLevel = CommunityLevel.Camp;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings — operates on the actor's CurrentCommunity.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (npc == null || npc.CharacterCommunity == null) return TaskStatus.Running;

            var community = npc.CharacterCommunity.CurrentCommunity;
            if (community == null) return TaskStatus.Running;

            var target = ResolveTargetTier();
            if (target == null) return TaskStatus.Running; // misconfigured asset; BT keeps retrying

            var current = community.CurrentTier;
            if (current == null) return TaskStatus.Running;

            // Compare by Order so the ladder ordering is authoritative even when the enum
            // mappings are stale (off-enum designer tiers).
            return current.Order >= target.Order
                ? TaskStatus.Completed
                : TaskStatus.Running;
        }

        public override void Cancel()
        {
            // Purely passive — nothing to clean up.
        }

        private CommunityTierRequirementsSO ResolveTargetTier()
        {
            if (TargetTier != null) return TargetTier;
            if (!string.IsNullOrEmpty(TargetTierId))
            {
                var byId = CommunityTierRegistry.GetById(TargetTierId);
                if (byId != null) return byId;
            }
            return CommunityTierRegistry.Get(TargetLevel);
        }
    }
}
