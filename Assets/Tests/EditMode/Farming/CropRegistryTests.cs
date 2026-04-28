using NUnit.Framework;
using UnityEngine;
using MWI.Farming;

namespace MWI.Tests.Farming
{
    public class CropRegistryTests
    {
        [TearDown]
        public void TearDown() => CropRegistry.Clear();

        [Test]
        public void Get_ReturnsNull_WhenNotInitialized()
        {
            CropRegistry.Clear();
            Assert.IsNull(CropRegistry.Get("anything"));
        }

        [Test]
        public void Get_ReturnsNull_ForUnknownId()
        {
            CropRegistry.InitializeForTests(new CropSO[0]);
            Assert.IsNull(CropRegistry.Get("nope"));
        }

        [Test]
        public void Get_ReturnsCropSO_AfterInitialize()
        {
            var crop = ScriptableObject.CreateInstance<CropSO>();
            crop.SetIdForTests("wheat");
            CropRegistry.InitializeForTests(new[] { crop });

            Assert.AreSame(crop, CropRegistry.Get("wheat"));
        }

        [Test]
        public void Get_NullId_ReturnsNull_WithoutThrow()
        {
            CropRegistry.InitializeForTests(new CropSO[0]);
            Assert.IsNull(CropRegistry.Get(null));
        }
    }
}
