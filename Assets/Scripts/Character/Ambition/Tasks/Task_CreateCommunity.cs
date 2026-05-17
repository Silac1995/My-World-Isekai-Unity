using System;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Ambition task: ensure the actor leads a community. Idempotent — re-Tick after the
    /// actor already leads is a guarded no-op (returns Completed immediately). Used as the
    /// first step of <c>Ambition_FoundACity</c>; Plan 4 chains <c>Task_PlaceBuilding</c>
    /// + <c>Task_FinishConstruction</c> next to build the AdministrativeBuilding.
    /// <para>
    /// Server-side only. Plan 1 stripped the trait + 4-friends gates from
    /// <c>CharacterCommunity.CheckAndCreateCommunity</c>; the sole remaining guard is "not
    /// already leading a community", which is also our Completed condition — so re-Tick
    /// after success short-circuits naturally.
    /// </para>
    /// </summary>
    [Serializable]
    public class Task_CreateCommunity : TaskBase
    {
        /// <summary>
        /// Optional override for the default community name. Default empty string defers
        /// to Plan 1's "{founder.Name}'s Settlement" pattern.
        /// </summary>
        public string CommunityName = string.Empty;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings — the task operates on the actor alone.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            // Defensive: null actor or missing subsystem keeps the BT trying.
            if (npc == null || npc.CharacterCommunity == null) return TaskStatus.Running;

            // Already leads a community → Completed (idempotent re-Tick).
            if (npc.CharacterCommunity.CurrentCommunity != null
                && npc.CharacterCommunity.CurrentCommunity.IsLeader(npc))
            {
                return TaskStatus.Completed;
            }

            // Drive the founding gesture. If CommunityName was set in the inspector,
            // use the named overload; otherwise the no-arg one applies the default
            // "{Name}'s Settlement" template.
            if (!string.IsNullOrEmpty(CommunityName))
            {
                npc.CharacterCommunity.CreateCommunity(CommunityName);
            }
            else
            {
                npc.CharacterCommunity.CheckAndCreateCommunity();
            }

            // Re-check post-action. CheckAndCreateCommunity is gated only by
            // "not already leading", so it should succeed unless the actor has
            // somehow been concurrently mutated (multi-leader race) — in which case
            // we report Running and the BT will retry next tick.
            return npc.CharacterCommunity.CurrentCommunity != null
                && npc.CharacterCommunity.CurrentCommunity.IsLeader(npc)
                ? TaskStatus.Completed
                : TaskStatus.Running;
        }

        public override void Cancel()
        {
            // No mid-pursuit state to clean up. Already-founded communities persist
            // (the founder may revisit the ambition later or pursue a parallel ambition).
        }
    }
}
