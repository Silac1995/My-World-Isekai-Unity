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
}
