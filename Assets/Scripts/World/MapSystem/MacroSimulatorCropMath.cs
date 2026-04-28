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
