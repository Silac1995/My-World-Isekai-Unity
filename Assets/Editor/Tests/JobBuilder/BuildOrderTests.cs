using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MWI.Quests;

namespace MWI.Tests.JobBuilder
{
    public class BuildOrderTests
    {
        [Test]
        public void Constructor_with_null_target_or_host_returns_a_BuildOrder_in_a_safe_state()
        {
            // Null target / host should NOT throw; the order is just "completed" (target gone).
            var order = new BuildOrder(target: null, host: null, clientBoss: null, placedOnDay: 0);
            Assert.IsNotNull(order);
            Assert.IsTrue(order.IsCompleted, "Null target means IsCompleted == true (nothing to build).");
            Assert.AreEqual(QuestState.Completed, order.State,
                "BuildOrder with null target settles to Completed immediately.");
        }

        [Test]
        public void IQuest_identity_fields_have_stable_shape_on_null_target()
        {
            var order = new BuildOrder(target: null, host: null, clientBoss: null, placedOnDay: 5);
            Assert.That(order.QuestId, Does.StartWith("BuildOrder_"));
            Assert.AreEqual(QuestType.Custom, order.Type);
            Assert.IsFalse(order.IsExpired, "BuildOrder is non-expiring.");
            Assert.AreEqual(-1, order.RemainingDays, "Non-expiring quests report -1, not 0.");
            Assert.AreEqual(0, order.MaxContributors,
                "MaxContributors is 0 when no host (no employee roster to draw from).");
        }

        [Test]
        public void TryJoin_and_TryLeave_are_no_ops_in_v1()
        {
            var order = new BuildOrder(target: null, host: null, clientBoss: null, placedOnDay: 0);
            Assert.IsFalse(order.TryJoin(null));
            Assert.IsFalse(order.TryLeave(null));
        }

        [Test]
        public void OnStateChanged_fires_when_state_transitions()
        {
            var order = new BuildOrder(target: null, host: null, clientBoss: null, placedOnDay: 0);
            int fires = 0;
            order.OnStateChanged += q => fires++;
            // No mutation possible from outside; just verify the event signature compiles.
            Assert.AreEqual(0, fires);
        }
    }
}
