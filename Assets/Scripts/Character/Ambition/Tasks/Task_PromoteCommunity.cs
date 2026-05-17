using System;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Passive ambition task: completes once the actor's community has reached
    /// <see cref="TargetLevel"/>. Does NOT drive the promotion — Plan 4's
    /// <c>Community.TryPromoteLevel()</c> mutator owns that. Use one instance per
    /// tier step in <c>Ambition_FoundACity</c>'s quest chain:
    /// <c>Task_PromoteCommunity(Camp)</c> → <c>Task_PromoteCommunity(Village)</c> → …
    /// </summary>
    [Serializable]
    public class Task_PromoteCommunity : TaskBase
    {
        /// <summary>Tier the actor's community must reach (>=) for this task to Complete.</summary>
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

            // CommunityLevel is an enum with ordered values (SmallGroup < Camp < Village < …).
            return community.level >= TargetLevel
                ? TaskStatus.Completed
                : TaskStatus.Running;
        }

        public override void Cancel()
        {
            // Purely passive — nothing to clean up.
        }
    }
}
