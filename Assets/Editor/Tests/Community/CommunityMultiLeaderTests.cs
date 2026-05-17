using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.Community
{
    public class CommunityMultiLeaderTests
    {
        private Character MakeBareCharacter(string name)
        {
            // Headless: a bare GameObject with a Character component is enough for
            // these reference-identity tests (Community stores Character refs).
            var go = new GameObject(name);
            return go.AddComponent<Character>();
        }

        [Test]
        public void Constructor_seeds_leaders_with_founder_only()
        {
            var founder = MakeBareCharacter("Founder");
            var c = new global::Community("Test", founder);

            Assert.AreEqual(1, c.leaders.Count, "Founder must be the only leader.");
            Assert.AreSame(founder, c.leaders[0]);
            Assert.AreSame(founder, c.PrimaryLeader);
            CollectionAssert.IsEmpty(c.SecondaryLeaders.ToList(), "No secondaries on fresh community.");
            Assert.IsTrue(c.IsLeader(founder));
            Assert.IsFalse(c.IsLeader(null), "IsLeader(null) must be false.");
        }

        [Test]
        public void IsLeader_returns_true_for_every_leader_in_the_roster()
        {
            var f = MakeBareCharacter("F");
            var s = MakeBareCharacter("S");
            var c = new global::Community("Test", f);
            c.AddMember(s);
            c.leaders.Add(s);

            Assert.IsTrue(c.IsLeader(f));
            Assert.IsTrue(c.IsLeader(s), "Secondary leader must satisfy IsLeader.");
            Assert.AreSame(f, c.PrimaryLeader);
            Assert.AreSame(s, c.SecondaryLeaders.First());
        }

        [Test]
        public void RemoveMember_removes_a_secondary_leader_without_changing_primary()
        {
            var f = MakeBareCharacter("F");
            var s = MakeBareCharacter("S");
            var c = new global::Community("Test", f);
            c.AddMember(s);
            c.leaders.Add(s);

            c.RemoveMember(s);

            Assert.IsFalse(c.leaders.Contains(s));
            Assert.AreSame(f, c.PrimaryLeader);
            Assert.IsFalse(c.IsLeader(s));
        }

        [Test]
        public void RemoveMember_when_primary_leaves_auto_promotes_first_secondary()
        {
            var f = MakeBareCharacter("F");
            var s = MakeBareCharacter("S");
            var c = new global::Community("Test", f);
            c.AddMember(s);
            c.leaders.Add(s);

            c.RemoveMember(f);

            Assert.AreEqual(1, c.leaders.Count);
            Assert.AreSame(s, c.PrimaryLeader,
                "Removing the primary at index 0 must shift the next leader into the primary slot.");
        }

        [Test]
        public void RemoveMember_when_sole_leader_leaves_leaves_community_leaderless()
        {
            var f = MakeBareCharacter("F");
            var c = new global::Community("Test", f);

            c.RemoveMember(f);

            Assert.AreEqual(0, c.leaders.Count);
            Assert.IsNull(c.PrimaryLeader);
            Assert.IsFalse(c.IsLeader(f));
        }
    }
}
