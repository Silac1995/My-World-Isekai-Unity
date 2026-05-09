using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.Farming
{
    public class HarvestablePredicateTests
    {
        private GameObject _go;
        private Harvestable _h;
        private MiscSO _toolA;
        private MiscSO _toolB;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestHarvestable");
            _h = _go.AddComponent<Harvestable>();
            _toolA = ScriptableObject.CreateInstance<MiscSO>();
            _toolB = ScriptableObject.CreateInstance<MiscSO>();
            _h.SetOutputItemsForTests(new System.Collections.Generic.List<ItemSO> { _toolA });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void CanHarvestWith_NoToolRequired_AlwaysAccepts()
        {
            _h.SetRequiredHarvestToolForTests(null);
            Assert.IsTrue(_h.CanHarvestWith(null));
            Assert.IsTrue(_h.CanHarvestWith(_toolA));
        }

        [Test]
        public void CanHarvestWith_RequiredToolMatch()
        {
            _h.SetRequiredHarvestToolForTests(_toolA);
            Assert.IsTrue(_h.CanHarvestWith(_toolA));
            Assert.IsFalse(_h.CanHarvestWith(null));
            Assert.IsFalse(_h.CanHarvestWith(_toolB));
        }

        [Test]
        public void CanDestroyWith_DefaultsOff()
        {
            Assert.IsFalse(_h.CanDestroyWith(null));
            Assert.IsFalse(_h.CanDestroyWith(_toolA));
        }

        [Test]
        public void CanDestroyWith_AllowedAndToolMatches()
        {
            _h.SetAllowDestructionForTests(true);
            _h.SetRequiredDestructionToolForTests(_toolA);
            Assert.IsTrue(_h.CanDestroyWith(_toolA));
            Assert.IsFalse(_h.CanDestroyWith(null));
        }

        [Test]
        public void CanDestroyWith_AllowedAndAnyToolWorks_WhenRequiredIsNull()
        {
            _h.SetAllowDestructionForTests(true);
            _h.SetRequiredDestructionToolForTests(null);
            Assert.IsTrue(_h.CanDestroyWith(null));
            Assert.IsTrue(_h.CanDestroyWith(_toolA));
        }
    }
}
