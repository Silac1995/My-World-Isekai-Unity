// Assets/Tests/EditMode/Orders/AuthorityResolverTests.cs
//
// NOTE: This assembly (Orders.Tests / MWI.Orders.Pure) cannot reference Assembly-CSharp,
// so AuthorityResolver.Resolve() (which depends on Character) cannot be tested here.
// The null-issuer / live-Character integration path is covered by PlayMode / integration tests.
// These tests validate the SO data contract and Resources.Load availability.
using NUnit.Framework;
using UnityEngine;
using MWI.Orders;

namespace MWI.Tests.Orders
{
    public class AuthorityResolverTests
    {
        [Test]
        public void StrangerAsset_LoadsFromResources_AndContextNameMatches()
        {
            // Validates the Stranger SO exists and has the correct ContextName field.
            // AuthorityResolver.Resolve(null, null) returns this SO — if it's missing,
            // all Order issuance breaks at runtime (AuthorityResolver logs an error).
            var ctx = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Stranger");
            Assert.IsNotNull(ctx, "Stranger asset must exist in Resources/Data/AuthorityContexts/");
            Assert.AreEqual("Stranger", ctx.ContextName);
        }

        [Test]
        public void AssetsLoadedFromResources_AllSevenPresent()
        {
            // Sanity: every v1 context asset must load.
            var contexts = new[] { "Stranger", "Friend", "Parent", "PartyLeader", "Employer", "Captain", "Lord" };
            foreach (var name in contexts)
            {
                var so = Resources.Load<AuthorityContextSO>($"Data/AuthorityContexts/Authority_{name}");
                Assert.IsNotNull(so, $"Missing asset: Authority_{name}.asset");
                Assert.AreEqual(name, so.ContextName, $"ContextName field on Authority_{name}.asset must equal '{name}'");
            }
        }

        [Test]
        public void StrangerHasBasePriority20()
        {
            var so = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Stranger");
            Assert.IsNotNull(so);
            Assert.AreEqual(20, so.BasePriority);
        }

        [Test]
        public void LordHasBasePriority85()
        {
            var so = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Lord");
            Assert.IsNotNull(so);
            Assert.AreEqual(85, so.BasePriority);
        }
    }
}
