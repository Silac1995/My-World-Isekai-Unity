using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.Community
{
    public class CitizenshipSaveRoundTripTests
    {
        [Test]
        public void CommunitySaveData_default_citizenshipMapId_is_null_or_empty()
        {
            var d = new CommunitySaveData();
            Assert.IsTrue(string.IsNullOrEmpty(d.citizenshipMapId),
                "Brand-new CommunitySaveData must default citizenshipMapId to null/empty so legacy saves deserialize cleanly.");
        }

        [Test]
        public void CommunitySaveData_citizenshipMapId_round_trips_through_JsonUtility()
        {
            var d = new CommunitySaveData
            {
                communityMapId = "current-map",
                citizenshipMapId = "citizen-map"
            };
            string json = JsonUtility.ToJson(d);
            var back = JsonUtility.FromJson<CommunitySaveData>(json);
            Assert.AreEqual("current-map", back.communityMapId);
            Assert.AreEqual("citizen-map", back.citizenshipMapId);
        }

        [Test]
        public void Legacy_json_without_citizenshipMapId_deserializes_to_empty()
        {
            // Snapshot of the pre-Plan-1 save shape — only communityMapId existed.
            string legacy = "{\"communityMapId\":\"legacy-map\"}";
            var d = JsonUtility.FromJson<CommunitySaveData>(legacy);
            Assert.AreEqual("legacy-map", d.communityMapId);
            Assert.IsTrue(string.IsNullOrEmpty(d.citizenshipMapId),
                "Legacy saves with no citizenshipMapId field must deserialize to null/empty (additive field, back-compatible).");
        }
    }
}
