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

    /// <summary>
    /// Owning building reference. Used both as a stable world-position anchor for
    /// <see cref="GetTaskWorldPosition"/> (the cell position needs a map resolve, and
    /// the building is by definition inside one map per BuildingPlacementManager rules)
    /// and as the IQuest <see cref="BuildingTarget"/> for the world-marker beacon.
    /// </summary>
    private readonly Building _building;

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
    /// (so the player can find the farm) AND <see cref="GetTaskWorldPosition"/> can resolve
    /// the cell's world position via the owning map's TerrainCellGrid. FarmingBuilding.PlantScan
    /// should pass `this`.
    /// </summary>
    public PlantCropTask(int cellX, int cellZ, CropSO crop, Building buildingForMarker = null) : base(null)
    {
        CellX = cellX;
        CellZ = cellZ;
        Crop = crop;
        _building = buildingForMarker;
        if (buildingForMarker != null)
            QuestTarget = new BuildingTarget(buildingForMarker);
    }

    /// <summary>
    /// Resolves the cell's world position via the owning map's TerrainCellGrid. Falls back
    /// to the building's own position (or <see cref="Vector3.zero"/> if no building was
    /// supplied). Used by BuildingTaskManager.ClaimBestTask for distance-based selection.
    /// </summary>
    public override Vector3 GetTaskWorldPosition()
    {
        if (_building == null) return Vector3.zero;
        var map = MWI.WorldSystem.MapController.GetMapAtPosition(_building.transform.position);
        var grid = map != null ? map.GetComponent<MWI.Terrain.TerrainCellGrid>() : null;
        return grid != null ? grid.GridToWorld(CellX, CellZ) : _building.transform.position;
    }

    public override bool IsValid()
    {
        // V1: valid as long as Crop is set. JobFarmer.Execute re-validates the cell-empty
        // precondition at claim time (race with another farmer or with rain mutating cell
        // state is handled there).
        return Crop != null;
    }
}
