using NUnit.Framework;
using UnityEngine;
using MWI.Farming;
using MWI.Terrain;

namespace MWI.Tests.Farming
{
    /// <summary>
    /// Pure-logic tests for the daily growth pipeline. Lives in Assets/Editor/Tests/ so it
    /// routes to Assembly-CSharp-Editor-testable (which auto-references Assembly-CSharp where
    /// TerrainCell lives + Pure asmdefs where CropSO/CropRegistry live).
    /// See farming spec §4 + §9.4.
    /// </summary>
    public class FarmGrowthPipelineTests
    {
        private CropSO _wheat;
        private CropSO _appleTree;

        [SetUp]
        public void SetUp()
        {
            _wheat = ScriptableObject.CreateInstance<CropSO>();
            _wheat.SetIdForTests("wheat");
            _wheat.SetDaysToMatureForTests(4);
            _wheat.SetMinMoistureForTests(0.3f);

            _appleTree = ScriptableObject.CreateInstance<CropSO>();
            _appleTree.SetIdForTests("apple");
            _appleTree.SetDaysToMatureForTests(4);
            _appleTree.SetMinMoistureForTests(0.3f);
            _appleTree.SetIsPerennialForTests(true);
            _appleTree.SetRegrowDaysForTests(2);

            CropRegistry.InitializeForTests(new[] { _wheat, _appleTree });
        }

        [TearDown]
        public void TearDown() => CropRegistry.Clear();

        [Test]
        public void GrowingCrop_WateredBelowThreshold_DoesNotAdvance()
        {
            var cell = MakeCell("wheat", growthTimer: 1f, moisture: 0.2f);
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(1f, cell.GrowthTimer);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Stalled, outcome);
        }

        [Test]
        public void GrowingCrop_WateredAtThreshold_Advances()
        {
            var cell = MakeCell("wheat", growthTimer: 1f, moisture: 0.3f);
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(2f, cell.GrowthTimer);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Grew, outcome);
        }

        [Test]
        public void GrowingCrop_CrossesMaturity_ReportsJustMatured_AndSetsReadySentinel()
        {
            var cell = MakeCell("wheat", growthTimer: 3f, moisture: 0.5f);
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(4f, cell.GrowthTimer);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.JustMatured, outcome);
        }

        [Test]
        public void LiveAndReady_NoOp()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.5f, timeSinceLastWatered: -1f);
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(4f, cell.GrowthTimer);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.NoOp, outcome);
        }

        [Test]
        public void DepletedPerennial_Watered_AdvancesRefillCounter()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.5f, timeSinceLastWatered: 0f);
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(1f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Refilling, outcome);
        }

        [Test]
        public void DepletedPerennial_HitsRegrowDays_FlipsToReady()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.5f, timeSinceLastWatered: 1f);
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.JustRefilled, outcome);
        }

        [Test]
        public void DepletedPerennial_Dry_DoesNotAdvance()
        {
            var cell = MakeCell("apple", growthTimer: 4f, moisture: 0.1f, timeSinceLastWatered: 0f);
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(0f, cell.TimeSinceLastWatered);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.Stalled, outcome);
        }

        [Test]
        public void OrphanCropId_ReturnsOrphan_DoesNotMutate()
        {
            var cell = MakeCell("nonexistent", growthTimer: 1f, moisture: 0.5f);
            var before = cell.GrowthTimer;
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(before, cell.GrowthTimer);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.OrphanCrop, outcome);
        }

        [Test]
        public void EmptyCell_NotPlanted()
        {
            var cell = new TerrainCell { IsPlowed = false, PlantedCropId = null };
            var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
            Assert.AreEqual(FarmGrowthPipeline.Outcome.NotPlanted, outcome);
        }

        private static TerrainCell MakeCell(string cropId, float growthTimer, float moisture, float timeSinceLastWatered = -1f)
            => new TerrainCell
            {
                IsPlowed = true,
                PlantedCropId = cropId,
                GrowthTimer = growthTimer,
                TimeSinceLastWatered = timeSinceLastWatered,
                Moisture = moisture
            };
    }
}
