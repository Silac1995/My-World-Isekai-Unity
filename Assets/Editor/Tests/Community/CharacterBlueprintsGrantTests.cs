using NUnit.Framework;
using MWI.WorldSystem;
using UnityEngine;

namespace MWI.Tests.Community
{
    public class CharacterBlueprintsGrantTests
    {
        private static BuildingSO MakeSO(string prefabId)
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            // BuildingSO._prefabId is private — set via reflection so the test
            // doesn't require a public test seam (mirrors how Buildings tests
            // construct SOs headlessly).
            typeof(BuildingSO)
                .GetField("_prefabId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(so, prefabId);
            return so;
        }

        [Test]
        public void GrantBlueprint_adds_PrefabId_to_unlocked_list_and_HasBlueprint_returns_true()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();
            var so = MakeSO("AdministrativeBuilding");

            Assert.IsFalse(bp.HasBlueprint(so));
            bp.GrantBlueprint(so);
            Assert.IsTrue(bp.HasBlueprint(so));
            Assert.IsTrue(bp.KnowsBlueprint("AdministrativeBuilding"),
                "String-keyed lookup must agree with SO-keyed lookup.");
        }

        [Test]
        public void GrantBlueprint_is_idempotent_no_duplicate_entries()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();
            var so = MakeSO("House");

            bp.GrantBlueprint(so);
            bp.GrantBlueprint(so);
            bp.GrantBlueprint(so);

            int countOfHouse = 0;
            foreach (var id in bp.UnlockedBuildingIds)
            {
                if (id == "House") countOfHouse++;
            }
            Assert.AreEqual(1, countOfHouse, "GrantBlueprint must be idempotent.");
        }

        [Test]
        public void GrantBlueprint_null_or_empty_is_noop()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();

            bp.GrantBlueprint(null);
            Assert.AreEqual(0, bp.UnlockedBuildingIds.Count,
                "GrantBlueprint(null) must be a silent no-op (defensive, rule #31).");

            var so = MakeSO(""); // empty PrefabId
            bp.GrantBlueprint(so);
            Assert.AreEqual(0, bp.UnlockedBuildingIds.Count,
                "GrantBlueprint(SO with empty PrefabId) must be a silent no-op.");
        }

        [Test]
        public void HasBlueprint_null_or_empty_returns_false()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();

            Assert.IsFalse(bp.HasBlueprint(null));

            var so = MakeSO("");
            Assert.IsFalse(bp.HasBlueprint(so));
        }
    }
}
