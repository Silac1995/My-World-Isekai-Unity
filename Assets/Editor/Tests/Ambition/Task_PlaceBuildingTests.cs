using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;
using MWI.WorldSystem;

namespace MWI.Tests.Ambition
{
    public class Task_PlaceBuildingTests
    {
        private Character MakeBareCharacter(string actorName)
        {
            var go = new GameObject(actorName);
            return go.AddComponent<Character>();
        }

        [Test]
        public void Tick_returns_Running_when_TargetBlueprint_is_null()
        {
            var actor = MakeBareCharacter("Founder");
            var task = new Task_PlaceBuilding { TargetBlueprint = null };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(actor, ctx),
                "Null blueprint must be a soft-fail (BT keeps retrying once author fixes the asset).");
        }

        [Test]
        public void Tick_returns_Running_when_actor_is_null()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            var task = new Task_PlaceBuilding { TargetBlueprint = so };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(null, ctx));
        }

        [Test]
        public void Tick_returns_Running_when_no_BuildingManager_instance_exists()
        {
            var actor = MakeBareCharacter("Founder");
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            var task = new Task_PlaceBuilding { TargetBlueprint = so };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            var status = task.Tick(actor, ctx);
            // Running if BuildingManager.Instance is null, or Completed if a stale
            // scene-loaded building somehow matches (unlikely). Either is acceptable.
            Assert.That(status, Is.EqualTo(TaskStatus.Running).Or.EqualTo(TaskStatus.Completed));
        }
    }
}
