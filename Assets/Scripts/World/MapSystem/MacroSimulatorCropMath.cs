using MWI.Farming;
using MWI.Terrain;

namespace MWI.WorldSystem.Simulation
{
    /// <summary>
    /// Pure offline catch-up math for one farming cell. See farming spec §9.4.
    /// Lives in Assembly-CSharp because it references TerrainCell + CropRegistry; tests
    /// live under Assets/Editor/Tests/Farming/ using the auto Editor-testable bridge.
    /// </summary>
    public static class MacroSimulatorCropMath
    {
        public static void AdvanceCellOffline(ref TerrainCell cell, int daysPassed, float estimatedAvgMoisture)
        {
            if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId)) return;
            var crop = CropRegistry.Get(cell.PlantedCropId);
            if (crop == null) return;
            if (estimatedAvgMoisture < crop.MinMoistureForGrowth) return;

            // PHASE A — still growing
            if (cell.GrowthTimer < crop.DaysToMature)
            {
                cell.GrowthTimer = System.Math.Min(cell.GrowthTimer + daysPassed, crop.DaysToMature);
                // If the offline tick crossed the maturity threshold, mirror what the live
                // FarmGrowthPipeline does on the JustMatured outcome: clear the watering
                // marker (TimeSinceLastWatered ≥ 0 means "watered while growing" during
                // PHASE A, but means "in refill cycle" during PHASE C). Without this flip,
                // a perennial that matured offline would be reconstructed by
                // FarmGrowthSystem.PostWakeSweep as startDepleted=true and never be
                // harvestable. Live-tick parity (see FarmGrowthPipeline.AdvanceOneDay).
                if (cell.GrowthTimer >= crop.DaysToMature)
                    cell.TimeSinceLastWatered = -1f;
                return;
            }

            // PHASE B — depleted perennial. One-shots have no offline state.
            if (crop.IsPerennial && cell.TimeSinceLastWatered >= 0f)
            {
                cell.TimeSinceLastWatered += daysPassed;
                if (cell.TimeSinceLastWatered >= crop.RegrowDays)
                    cell.TimeSinceLastWatered = -1f;
            }
        }
    }
}
