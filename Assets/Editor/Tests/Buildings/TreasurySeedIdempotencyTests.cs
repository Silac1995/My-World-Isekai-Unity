using NUnit.Framework;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Tests.Buildings
{
    public class TreasurySeedIdempotencyTests
    {
        [Test]
        public void CommercialBuilding_exposes_TreasurySeeded_default_false()
        {
            var go = new GameObject("TestShop");
            try
            {
                var b = go.AddComponent<TestShopProbe>();
                Assert.IsFalse(b.TreasurySeededProbe,
                    "TreasurySeeded must default false on a fresh build so the construction-complete hook fires once.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Lightweight probe to expose the internal flag without spinning up the full ShopBuilding.
        // CommercialBuilding is abstract, so we subclass it concretely here. InitializeJobs() is the
        // only abstract member — implement as no-op for the headless test. The probe is editor-only
        // and lives only inside the Assembly-CSharp-Editor assembly the test file is compiled into.
        private class TestShopProbe : CommercialBuilding
        {
            public bool TreasurySeededProbe => GetTreasurySeededForTests();
            protected override void InitializeJobs() { /* no-op for headless flag-default test */ }
        }
    }

    public class BuildingSaveDataTreasurySeededRoundTripTests
    {
        [Test]
        public void BuildingSaveData_round_trips_TreasurySeeded_through_JsonUtility()
        {
            var data = new BuildingSaveData { BuildingId = "x", PrefabId = "y", TreasurySeeded = true };
            var json = UnityEngine.JsonUtility.ToJson(data);
            var back = UnityEngine.JsonUtility.FromJson<BuildingSaveData>(json);
            Assert.IsTrue(back.TreasurySeeded);
        }

        [Test]
        public void BuildingSaveData_TreasurySeeded_defaults_false_when_field_missing_from_json()
        {
            // Synthetic "old save" JSON — no TreasurySeeded key. Verifies legacy save compat.
            const string oldJson = "{\"BuildingId\":\"x\",\"PrefabId\":\"y\"}";
            var data = UnityEngine.JsonUtility.FromJson<BuildingSaveData>(oldJson);
            Assert.IsFalse(data.TreasurySeeded, "Missing TreasurySeeded in JSON must default false so re-seed fires once.");
        }
    }
}
