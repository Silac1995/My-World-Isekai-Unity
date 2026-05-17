using System;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Ambition
{
    /// <summary>
    /// Passive ambition task: completes once the actor has placed a building whose
    /// blueprint matches <see cref="TargetBlueprint"/>. The placement itself is driven
    /// by either the player's ghost flow (BuildingPlacementManager) or an NPC BTAction
    /// (Plan 4b). This task only watches the world state — it never drives placement.
    /// <para>
    /// "Placed" means: <see cref="BuildingManager.allBuildings"/> contains an instance
    /// whose <see cref="Building.Blueprint"/> equals TargetBlueprint AND whose
    /// <see cref="Building.PlacedByCharacterId"/> matches <c>actor.CharacterId</c>.
    /// The building may still be under construction — Task_FinishConstruction handles
    /// the next step.
    /// </para>
    /// </summary>
    [Serializable]
    public class Task_PlaceBuilding : TaskBase
    {
        [Tooltip("The BuildingSO the actor must place for this task to complete.")]
        public BuildingSO TargetBlueprint;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings — the task is anchored to actor + TargetBlueprint.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (npc == null) return TaskStatus.Running;
            if (TargetBlueprint == null) return TaskStatus.Running;  // misconfigured asset; BT keeps retrying

            string actorId = npc.CharacterId;
            if (string.IsNullOrEmpty(actorId)) return TaskStatus.Running;

            var bm = BuildingManager.Instance;
            if (bm == null) return TaskStatus.Running;

            // Scan for any building whose blueprint matches AND was placed by this actor.
            // BuildingManager.allBuildings is server-side; this task runs server-side (BT tick).
            for (int i = 0; i < bm.allBuildings.Count; i++)
            {
                var b = bm.allBuildings[i];
                if (b == null) continue;
                if (b.Blueprint != TargetBlueprint) continue;
                if (b.PlacedByCharacterId.Value.ToString() != actorId) continue;
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
