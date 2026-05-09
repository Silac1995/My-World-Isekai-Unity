using NUnit.Framework;
using UnityEngine;
using MWI.Farming;

namespace MWI.Tests.Farming
{
    public class CropSOValidationTests
    {
        [Test]
        public void GetStageSprite_OutOfRange_ClampsDefensively()
        {
            var crop = ScriptableObject.CreateInstance<CropSO>();
            Assert.IsNull(crop.GetStageSprite(0));
            Assert.IsNull(crop.GetStageSprite(99));
        }

        [Test]
        public void Defaults_AreSensible()
        {
            var crop = ScriptableObject.CreateInstance<CropSO>();
            Assert.AreEqual(4, crop.DaysToMature);
            Assert.AreEqual(0.3f, crop.MinMoistureForGrowth);
            Assert.IsFalse(crop.IsPerennial);
            Assert.IsFalse(crop.AllowDestruction);
        }
    }
}
