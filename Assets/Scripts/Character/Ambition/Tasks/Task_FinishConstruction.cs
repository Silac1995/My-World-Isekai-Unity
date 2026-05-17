using System;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Ambition
{
    /// <summary>
    /// Passive ambition task: completes once a building matching
    /// <see cref="TargetBlueprint"/> placed by the actor has reached the Complete
    /// construction state. Re-uses the cooperative construction loop (Phase 1) —
    /// the task never drives the construction itself; player or NPC drives via
    /// CharacterAction_FinishConstruction.
    /// </summary>
    [Serializable]
    public class Task_FinishConstruction : TaskBase
    {
        [Tooltip("The BuildingSO to watch. Same as the preceding Task_PlaceBuilding's TargetBlueprint.")]
        public BuildingSO TargetBlueprint;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (npc == null) return TaskStatus.Running;
            if (TargetBlueprint == null) return TaskStatus.Running;

            string actorId = npc.CharacterId;
            if (string.IsNullOrEmpty(actorId)) return TaskStatus.Running;

            var bm = BuildingManager.Instance;
            if (bm == null) return TaskStatus.Running;

            // Completed = an actor-placed building of TargetBlueprint that is NO LONGER
            // under construction. If the actor placed multiple instances (edge case —
            // there's no rule against re-placement after a destruction), the first
            // complete one wins; the task does not require ALL of them to complete.
            for (int i = 0; i < bm.allBuildings.Count; i++)
            {
                var b = bm.allBuildings[i];
                if (b == null) continue;
                if (b.Blueprint != TargetBlueprint) continue;
                if (b.PlacedByCharacterId.Value.ToString() != actorId) continue;
                if (b.IsUnderConstruction) continue;
                return TaskStatus.Completed;
            }
            return TaskStatus.Running;
        }

        public override void Cancel()
        {
            // Passive — nothing to clean up.
        }
    }
}
