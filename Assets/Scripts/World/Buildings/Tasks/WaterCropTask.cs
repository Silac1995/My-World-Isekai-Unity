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

    public override int MaxWorkers => 1;

    public override string Title => "Water Crop";
    public override string InstructionLine => $"Water the cell at ({CellX}, {CellZ})";
    public override string Description => "Water a thirsty crop with the watering can from the building's tool storage.";
    public override int Required => 1;

    public WaterCropTask(int cellX, int cellZ, Building buildingForMarker = null) : base(null)
    {
        CellX = cellX;
        CellZ = cellZ;
        if (buildingForMarker != null)
            QuestTarget = new BuildingTarget(buildingForMarker);
    }

    public override bool IsValid()
    {
        // V1: always valid; JobFarmer.Execute re-validates cell state (still planted, still
        // dry, not mature) at claim time.
        return true;
    }
}
