/// <summary>
/// Server-only data class describing a build material that the city's logistics chain
/// failed to source through normal channels (B2B shop scan, crafter producer chain,
/// VirtualResourceSupplier). Lives on
/// <see cref="AdministrativeBuilding"/>'s _unfulfillableMaterialHarvestQueue and is
/// consumed by <see cref="JobHarvester"/>'s CityHarvester runtime branch (Plan 4b
/// Task 7) — which scans for a nearby <see cref="Harvestable"/> yielding
/// <see cref="Item"/> and runs the existing harvest→pickup→deposit chain.
///
/// Not networked — JobBuilder + JobHarvester + JobLogisticsManager all execute server-
/// side, so client visibility is not needed.
/// </summary>
[System.Serializable]
public class UnfulfillableMaterial
{
    public ItemSO Item;
    public int Qty;
    public int LastEnqueuedDay;

    public UnfulfillableMaterial(ItemSO item, int qty, int day)
    {
        Item = item;
        Qty = qty;
        LastEnqueuedDay = day;
    }
}
