using NUnit.Framework;
using MWI.Economy;
using MWI.WorldSystem;
using UnityEngine;

namespace MWI.Tests.Buildings
{
    public class CommunityDataNativeCurrencyTests
    {
        [Test]
        public void CommunityData_default_NativeCurrency_is_CurrencyId_Default()
        {
            var c = new CommunityData();
            Assert.AreEqual(CurrencyId.Default, c.NativeCurrency,
                "Brand-new CommunityData must default NativeCurrency to CurrencyId.Default so legacy saves load with sane behaviour.");
        }

        [Test]
        public void CommunityData_NativeCurrency_round_trips_through_JsonUtility()
        {
            var c = new CommunityData { MapId = "test-map", NativeCurrency = new CurrencyId(42) };
            var json = JsonUtility.ToJson(c);
            var back = JsonUtility.FromJson<CommunityData>(json);
            Assert.AreEqual(new CurrencyId(42), back.NativeCurrency);
        }
    }

    public class MapControllerNativeCurrencyTests
    {
        [Test]
        public void MapController_NativeCurrency_falls_back_to_Default_when_no_community()
        {
            // Headless: spawn a bare MapController GameObject without a CommunityData entry.
            var go = new GameObject("TestMap");
            var map = go.AddComponent<MapController>();
            try
            {
                Assert.AreEqual(CurrencyId.Default, map.NativeCurrency,
                    "MapController without a registered CommunityData must return CurrencyId.Default.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public class BuildingSOAuthoringTests
    {
        [Test]
        public void BuildingSO_default_fields_are_safe()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            try
            {
                Assert.IsTrue(string.IsNullOrEmpty(so.PrefabId), "PrefabId must default to empty (designer must author it explicitly).");
                Assert.AreEqual(0, so.CommunityPriority);
                Assert.IsNull(so.BuildingPrefab);
                Assert.IsNull(so.InteriorPrefab);
                Assert.IsNull(so.Icon);
                Assert.IsNotNull(so.ConstructionRequirements);
                Assert.AreEqual(0, so.ConstructionRequirements.Count);
                Assert.IsNotNull(so.DefaultFurnitureLayout);
                Assert.AreEqual(0, so.DefaultFurnitureLayout.Count);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }
    }

    public class BuildingCommercialSOTests
    {
        [Test]
        public void BuildingCommercialSO_default_BaseTreasury_is_zero()
        {
            var so = ScriptableObject.CreateInstance<BuildingCommercialSO>();
            try { Assert.AreEqual(0, so.BaseTreasury); }
            finally { Object.DestroyImmediate(so); }
        }

        [Test]
        public void BuildingCommercialSO_is_assignable_to_BuildingSO_field()
        {
            var so = ScriptableObject.CreateInstance<BuildingCommercialSO>();
            try
            {
                BuildingSO asBase = so;
                Assert.IsNotNull(asBase, "Subclass must be substitutable for base (SOLID LSP — rule #11).");
            }
            finally { Object.DestroyImmediate(so); }
        }
    }
}
