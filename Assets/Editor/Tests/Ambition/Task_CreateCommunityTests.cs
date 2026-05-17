using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class Task_CreateCommunityTests
    {
        /// <summary>
        /// Builds a headless Character + CharacterCommunity pair with all cross-references
        /// manually wired.
        ///
        /// EditMode lifecycle constraints:
        ///   1. Character.Awake() aborts early (ValidateRequiredComponents fails — no
        ///      Rigidbody/Collider on a bare GO), so _characterCommunity is never
        ///      field-scanned.
        ///   2. CharacterSystem extends NetworkBehaviour; without a NetworkManager,
        ///      NGO intercepts OnEnable so CharacterSystem.OnEnable (which calls
        ///      character.Register(this)) does not reliably fire.
        ///   3. CharacterCommunity.Awake() is private (shadows CharacterSystem.Awake)
        ///      and calls GetComponent<Character>() — but NGO may defer the Awake
        ///      dispatch in headless mode.
        ///
        /// Fix: explicitly Register the subsystem (populates TryGet path) and set
        /// the protected _character field via reflection (satisfies null-guards in
        /// CheckAndCreateCommunity / CreateCommunity).
        /// </summary>
        private (Character character, CharacterCommunity community) MakeCharacterWithCommunity(string actorName)
        {
            var go = new GameObject(actorName);
            go.name = actorName; // CharacterName falls back to gameObject.name in some paths

            var character = go.AddComponent<Character>();
            var community = go.AddComponent<CharacterCommunity>();

            // Wire _character inside CharacterCommunity (protected field from CharacterSystem).
            var charField = typeof(CharacterSystem).GetField(
                "_character", BindingFlags.NonPublic | BindingFlags.Instance);
            charField?.SetValue(community, character);

            // Register so character.CharacterCommunity → TryGet resolves.
            character.Register(community);

            return (character, community);
        }

        [Test]
        public void Tick_returns_Completed_when_actor_already_leads_a_community()
        {
            var (actor, _) = MakeCharacterWithCommunity("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);

            // Pre-seed: actor already leads a community.
            var pre = new global::Community("Pre-existing", actor);
            actor.CharacterCommunity.SetCurrentCommunity(pre);
            Assert.IsTrue(pre.IsLeader(actor));

            var status = task.Tick(actor, ctx);
            Assert.AreEqual(TaskStatus.Completed, status);
        }

        [Test]
        public void Tick_returns_Completed_after_founding_a_new_community()
        {
            var (actor, _) = MakeCharacterWithCommunity("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);

            Assert.IsNull(actor.CharacterCommunity.CurrentCommunity);

            var status = task.Tick(actor, ctx);

            Assert.AreEqual(TaskStatus.Completed, status);
            Assert.IsNotNull(actor.CharacterCommunity.CurrentCommunity,
                "Tick must invoke CheckAndCreateCommunity so the actor now leads a community.");
            Assert.IsTrue(actor.CharacterCommunity.CurrentCommunity.IsLeader(actor));
        }

        [Test]
        public void Tick_is_idempotent_on_repeat_invocation()
        {
            var (actor, _) = MakeCharacterWithCommunity("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);

            var first = task.Tick(actor, ctx);
            var foundedCommunity = actor.CharacterCommunity.CurrentCommunity;

            var second = task.Tick(actor, ctx);
            var third = task.Tick(actor, ctx);

            Assert.AreEqual(TaskStatus.Completed, first);
            Assert.AreEqual(TaskStatus.Completed, second);
            Assert.AreEqual(TaskStatus.Completed, third);
            Assert.AreSame(foundedCommunity, actor.CharacterCommunity.CurrentCommunity,
                "Repeat Ticks must not re-create the community.");
        }

        [Test]
        public void Tick_with_null_actor_returns_Running_defensively()
        {
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity();
            task.Bind(ctx);
            Assert.AreEqual(TaskStatus.Running, task.Tick(null, ctx),
                "Null actor must not throw; return Running so the BT keeps trying.");
        }

        [Test]
        public void CommunityName_field_overrides_default_settlement_name()
        {
            var (actor, _) = MakeCharacterWithCommunity("Founder");
            var ctx = new AmbitionContext();
            var task = new Task_CreateCommunity { CommunityName = "Citadel of Light" };
            task.Bind(ctx);

            task.Tick(actor, ctx);

            Assert.AreEqual("Citadel of Light", actor.CharacterCommunity.CurrentCommunity.communityName);
        }
    }
}
