using NUnit.Framework;
using UnityEngine;
using MWI.Farming;
using MWI.Terrain;
using MWI.WorldSystem.Simulation;

namespace MWI.Tests.Farming
{
    /// <summary>
    /// Pure offline-catch-up math for one cell. See farming spec §9.4.
    /// Integration with MacroSimulator orchestration is tested manually via the
    /// hibernation acceptance criterion (§12.9).
    /// </summary>
    public class MacroSimulatorCropMathTests
    {
        private CropSO _wheat;
        private CropSO _apple;

        [SetUp]
        public void SetUp()
        {
            _wheat = MakeCrop("wheat", days: 4, perennial: false);
            _apple = MakeCrop("apple", days: 4, perennial: true, regrow: 2);
            CropRegistry.InitializeForTests(new[] { _wheat, _apple });
        }

        [TearDown]
        public void TearDown() => CropRegistry.Clear();

        [Test]
        public void GrowingCrop_WetClimate_AdvancesByDaysPassed_ClampedAtMaturity()
        {
            var cell = MakeCell("wheat", growthTimer: 1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 5, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(4f, cell.GrowthTimer); // clamped at DaysToMature
        }

        [Test]
        public void GrowingCrop_DryClimate_DoesNotAdvance()
        {
            var cell = MakeCell("wheat", growthTimer: 1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 5, estimatedAvgMoisture: 0.1f);
            Assert.AreEqual(1f, cell.GrowthTimer);
        }

        [Test]
        public void DepletedPerennial_WetClimate_RefillsExactlyOnce()
        {
            var cell = MakeCell("apple", growthTimer: 4f, timeSinceLastWatered: 0f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 7, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
        }

        [Test]
        public void DepletedPerennial_DryClimate_DoesNotAdvance()
        {
            var cell = MakeCell("apple", growthTimer: 4f, timeSinceLastWatered: 0f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 7, estimatedAvgMoisture: 0.1f);
            Assert.AreEqual(0f, cell.TimeSinceLastWatered);
        }

        [Test]
        public void OneShotMature_NoOfflineState_LeftAlone()
        {
            var cell = MakeCell("wheat", growthTimer: 4f, timeSinceLastWatered: -1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 30, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(4f, cell.GrowthTimer);
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered);
        }

        [Test]
        public void GrowingPerennial_WateredThenCrossesMaturity_ClearsTimeSinceLastWateredSentinel()
        {
            // Repro for the 2026-05-02 save/load bug: a perennial was planted, watered
            // (TimeSinceLastWatered=0), saved at GrowthTimer=1, then offline ticks crossed
            // the maturity threshold. Without clearing the sentinel here,
            // FarmGrowthSystem.PostWakeSweep would see TimeSinceLastWatered=0 + IsPerennial
            // and reconstruct the harvestable as startDepleted=true (stuck-IsDepleted),
            // because TimeSinceLastWatered is overloaded — it's a "watered while growing"
            // marker in PHASE A and a "refill counter" in PHASE C. Live tick (FarmGrowthPipeline.
            // AdvanceOneDay's JustMatured branch) clears it; the offline path now mirrors that.
            var cell = MakeCell("apple", growthTimer: 1f, timeSinceLastWatered: 0f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 5, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(4f, cell.GrowthTimer, "GrowthTimer should clamp at DaysToMature.");
            Assert.AreEqual(-1f, cell.TimeSinceLastWatered, "TimeSinceLastWatered should be reset to the 'ready' sentinel after crossing maturity, mirroring FarmGrowthPipeline.AdvanceOneDay's JustMatured branch.");
        }

        [Test]
        public void GrowingPerennial_DoesNotCrossMaturity_LeavesTimeSinceLastWateredAsIs()
        {
            // Sanity check: if growth doesn't cross the maturity boundary, TimeSinceLastWatered
            // should stay where it was (mirrors live tick PHASE A which never touches it).
            var cell = MakeCell("apple", growthTimer: 0f, timeSinceLastWatered: 0f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 2, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(2f, cell.GrowthTimer);
            Assert.AreEqual(0f, cell.TimeSinceLastWatered, "Pre-maturity, TimeSinceLastWatered is the 'watered while growing' marker — should not be flipped.");
        }

        [Test]
        public void Orphan_NoMutation()
        {
            var cell = MakeCell("nonexistent", growthTimer: 1f);
            MacroSimulatorCropMath.AdvanceCellOffline(ref cell, daysPassed: 5, estimatedAvgMoisture: 0.5f);
            Assert.AreEqual(1f, cell.GrowthTimer);
        }

        private static CropSO MakeCrop(string id, int days, bool perennial = false, int regrow = 0)
        {
            var c = ScriptableObject.CreateInstance<CropSO>();
            c.SetIdForTests(id);
            c.SetDaysToMatureForTests(days);
            c.SetMinMoistureForTests(0.3f);
            c.SetIsPerennialForTests(perennial);
            c.SetRegrowDaysForTests(regrow);
            return c;
        }

        private static TerrainCell MakeCell(string cropId, float growthTimer, float timeSinceLastWatered = -1f)
            => new TerrainCell
            {
                IsPlowed = true,
                PlantedCropId = cropId,
                GrowthTimer = growthTimer,
                TimeSinceLastWatered = timeSinceLastWatered
            };
    }
}
