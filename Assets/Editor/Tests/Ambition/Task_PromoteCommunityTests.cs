using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class Task_PromoteCommunityTests
    {
        /// <summary>
        /// Same headless-wiring helper as Task_CreateCommunityTests. EditMode lifecycle
        /// constraints: Character.Awake aborts early (no Rigidbody/Collider on bare GO);
        /// CharacterSystem.OnEnable is intercepted by NGO without a NetworkManager.
        /// We reflectively set CharacterSystem._character and explicitly call
        /// Character.Register(...) to make TryGet resolve.
        /// </summary>
        private (Character character, CharacterCommunity community) MakeCharacterWithCommunity(string actorName)
        {
            var go = new GameObject(actorName);
            go.name = actorName;

            var character = go.AddComponent<Character>();
            var community = go.AddComponent<CharacterCommunity>();

            var charField = typeof(CharacterSystem).GetField(
                "_character", BindingFlags.NonPublic | BindingFlags.Instance);
            charField?.SetValue(community, character);

            character.Register(community);

            return (character, community);
        }

        [Test]
        public void Tick_returns_Running_when_actor_has_no_community()
        {
            var (actor, _) = MakeCharacterWithCommunity("Lonely");
            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(actor, ctx));
        }

        [Test]
        public void Tick_returns_Running_when_community_below_target_level()
        {
            var (actor, _) = MakeCharacterWithCommunity("Founder");
            var community = new global::Community("Test", actor);
            // Community ctor sets level = CommunityLevel.SmallGroup.
            actor.CharacterCommunity.SetCurrentCommunity(community);

            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(actor, ctx),
                "Community at SmallGroup with TargetLevel=Camp must be Running.");
        }

        [Test]
        public void Tick_returns_Completed_when_community_at_or_above_target_level()
        {
            var (actor, _) = MakeCharacterWithCommunity("Founder");
            var community = new global::Community("Test", actor);
            community.ChangeLevel(CommunityLevel.Camp);
            actor.CharacterCommunity.SetCurrentCommunity(community);

            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Completed, task.Tick(actor, ctx));
        }

        [Test]
        public void Tick_passive_does_not_mutate_community_level()
        {
            var (actor, _) = MakeCharacterWithCommunity("Founder");
            var community = new global::Community("Test", actor);
            actor.CharacterCommunity.SetCurrentCommunity(community);

            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            task.Tick(actor, ctx);
            Assert.AreEqual(CommunityLevel.SmallGroup, community.level,
                "Task_PromoteCommunity must be passive — it watches level but never sets it.");
        }

        [Test]
        public void Tick_with_null_actor_returns_Running_defensively()
        {
            var task = new Task_PromoteCommunity { TargetLevel = CommunityLevel.Camp };
            var ctx = new AmbitionContext();
            task.Bind(ctx);
            Assert.AreEqual(TaskStatus.Running, task.Tick(null, ctx));
        }
    }
}
