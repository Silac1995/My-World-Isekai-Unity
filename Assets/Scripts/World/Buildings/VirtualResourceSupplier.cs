using UnityEngine;
using MWI.WorldSystem;
using System.Collections.Generic;

public class VirtualResourceSupplier : CommercialBuilding
{
    [Header("Virtual Harvesting")]
    public string ResourceId;           // matches ResourcePoolEntry.ResourceId
    public MapController ParentMap;     // reference to this map's controller

    protected override void InitializeJobs()
    {
        // No explicit jobs needed; offline logic drives the yield.
    }

    public void Initialize(string resourceId, MapController parentMap)
    {
        ResourceId = resourceId;
        ParentMap = parentMap;
        buildingName = $"Virtual Node: {resourceId}";
    }

    public override bool ProducesItem(ItemSO item)
    {
        return item != null && item.name == ResourceId; 
    }

    public override bool TryFulfillOrder(BuyOrder order, int amount)
    {
        if (ParentMap == null) return false;

        var pool = ParentMap.GetResourcePool(ResourceId);
        if (pool == null || pool.CurrentAmount < amount) return false;

        pool.CurrentAmount -= amount;

        // Generate the physical instances so the transporter can pick them up
        for (int i = 0; i < amount; i++)
        {
            var itemInstance = order.ItemToTransport.CreateInstance();
            AddToInventory(itemInstance);
        }
        
        return true;
    }
}
