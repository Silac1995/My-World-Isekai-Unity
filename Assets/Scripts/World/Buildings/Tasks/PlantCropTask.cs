using UnityEngine;
using MWI.Farming;
using MWI.Quests;

/// <summary>
/// BuildingTask: plant <see cref="Crop"/> at the given terrain cell. Registered by
/// FarmingBuilding.PlantScan once per OnNewDay for empty cells inside the farm zones.
/// Claimed + executed by JobFarmer's GoapAction_PlantCrop chain (FetchSeed -> PlantCrop).
///
/// Auto-publishes as IQuest via existing CommercialBuilding quest aggregator (Hybrid C
/// unification, 2026-04-23). Quest eligibility maps to JobType.Farmer via Task 2.
///
/// Target = null (cell-targeted, not MonoBehaviour-targeted). QuestTarget can be the
/// owning building (so the world-marker beacon points at the farm) — set via the
/// optional buildingForMarker constructor param.
/// </summary>
public class PlantCropTask : BuildingTask
{
    public int CellX { get; }
    public int CellZ { get; }
    public CropSO Crop { get; }

    public override int MaxWorkers => 1; // one farmer per cell at a time

    public override string Title => "Plant Crop";
    public override string InstructionLine => Crop != null ? $"Plant {Crop.Id} at ({CellX}, {CellZ})" : Title;
    public override string Description => Crop != null
        ? $"Plant a {Crop.Id} seed in the prepared cell."
        : "Plant a crop in the prepared cell.";
    public override int Required => 1;

    /// <summary>
    /// Construct a plant task. <paramref name="buildingForMarker"/> is optional but
    /// recommended — when supplied, the IQuest world-marker beacon shows at the building
    /// (so the player can find the farm). FarmingBuilding.PlantScan should pass `this`.
    /// </summary>
    public PlantCropTask(int cellX, int cellZ, CropSO crop, Building buildingForMarker = null) : base(null)
    {
        CellX = cellX;
        CellZ = cellZ;
        Crop = crop;
        if (buildingForMarker != null)
            QuestTarget = new BuildingTarget(buildingForMarker);
    }

    public override bool IsValid()
    {
        // V1: valid as long as Crop is set. JobFarmer.Execute re-validates the cell-empty
        // precondition at claim time (race with another farmer or with rain mutating cell
        // state is handled there).
        return Crop != null;
    }
}
