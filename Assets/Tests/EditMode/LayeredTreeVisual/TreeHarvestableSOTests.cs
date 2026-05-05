using NUnit.Framework;
using UnityEngine;
using MWI.Interactables;

namespace MWI.Tests.LayeredTreeVisual
{
    public class TreeHarvestableSOTests
    {
        [Test]
        public void Defaults_HaveSensibleValues()
        {
            var so = ScriptableObject.CreateInstance<TreeHarvestableSO>();
            Assert.IsNull(so.TrunkSprite);
            Assert.IsNull(so.FoliageSprite);
            Assert.IsNotNull(so.FoliageColorOverYear, "Gradient should be auto-initialised by Unity.");
            Assert.IsNotNull(so.FruitSpriteVariants);
            Assert.AreEqual(0, so.FruitSpriteVariants.Length);
            Assert.AreEqual(Rect.zero, so.FruitSpawnArea);
            Assert.AreEqual(Vector2.one, so.FruitScale);
        }

        [Test]
        public void InheritsHarvestableSOFields()
        {
            var so = ScriptableObject.CreateInstance<TreeHarvestableSO>();
            Assert.IsNotNull(so.HarvestOutputs);
            Assert.IsTrue(so.IsDepletable);
            Assert.AreEqual(5, so.MaxHarvestCount);
        }
    }
}
