using MWI.Terrain;

namespace MWI.Farming
{
    /// <summary>
    /// Pure C# daily-tick logic for one cell. No Unity dependencies beyond TerrainCell.
    /// FarmGrowthSystem is the MonoBehaviour wrapper that calls this for every cell.
    /// See farming spec §4 and §9.2.
    /// </summary>
    public static class FarmGrowthPipeline
    {
        public enum Outcome
        {
            NotPlanted,
            OrphanCrop,
            Stalled,
            Grew,
            JustMatured,
            NoOp,
            Refilling,
            JustRefilled,
        }

        public static Outcome AdvanceOneDay(ref TerrainCell cell)
        {
            if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId))
                return Outcome.NotPlanted;

            var crop = CropRegistry.Get(cell.PlantedCropId);
            if (crop == null) return Outcome.OrphanCrop;

            // PHASE A — still growing
            if (cell.GrowthTimer < crop.DaysToMature)
            {
                if (cell.Moisture < crop.MinMoistureForGrowth) return Outcome.Stalled;
                cell.GrowthTimer += 1f;
                if (cell.GrowthTimer >= crop.DaysToMature)
                {
                    cell.GrowthTimer = crop.DaysToMature;
                    cell.TimeSinceLastWatered = -1f;
                    return Outcome.JustMatured;
                }
                return Outcome.Grew;
            }

            // PHASE B — live harvestable, ready
            if (cell.TimeSinceLastWatered < 0f) return Outcome.NoOp;

            // PHASE C — live harvestable, depleted (perennial only by construction).
            if (cell.Moisture < crop.MinMoistureForGrowth) return Outcome.Stalled;
            cell.TimeSinceLastWatered += 1f;
            if (cell.TimeSinceLastWatered >= crop.RegrowDays)
            {
                cell.TimeSinceLastWatered = -1f;
                return Outcome.JustRefilled;
            }
            return Outcome.Refilling;
        }
    }
}
