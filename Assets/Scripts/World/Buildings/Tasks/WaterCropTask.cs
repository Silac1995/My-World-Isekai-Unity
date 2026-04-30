using UnityEngine;
using MWI.Quests;

/// <summary>
/// BuildingTask: water the planted crop at the given terrain cell. Registered by
/// FarmingBuilding.WaterScan once per OnNewDay for cells whose
/// TimeSinceLastWatered >= 1f AND Moisture &lt; crop.MinMoistureForGrowth AND
/// GrowthTimer &lt; crop.DaysToMature (mature/perennial crops don't water).
///
/// Claimed by JobFarmer's GoapAction_WaterCrop chain (FetchToolFromStorage(WateringCan)
/// -> WaterCrop -> ReturnToolToStorage). The WateringCan is fetched from the building's
/// _toolStorageFurniture (Plan 1 ToolStorage primitive).
/// </summary>
public class WaterCropTask : BuildingTask
{
    public int CellX { get; }
    public int CellZ { get; }

    /// <summary>
    /// Owning building reference. Used both as a stable world-position anchor for
    /// <see cref="GetTaskWorldPosition"/> (the cell position needs a map resolve, and
    /// the building is by definition inside one map per BuildingPlacementManager rules)
    /// and as the IQuest <see cref="BuildingTarget"/> for the world-marker beacon.
    /// </summary>
    private readonly Building _building;

    public override int MaxWorkers => 1;

    public override string Title => "Water Crop";
    public override string InstructionLine => $"Water the cell at ({CellX}, {CellZ})";
    public override string Description => "Water a thirsty crop with the watering can from the building's tool storage.";
    public override int Required => 1;

    public WaterCropTask(int cellX, int cellZ, Building buildingForMarker = null) : base(null)
    {
        CellX = cellX;
        CellZ = cellZ;
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
        // V1: always valid; JobFarmer.Execute re-validates cell state (still planted, still
        // dry, not mature) at claim time.
        return true;
    }
}
