using System.Collections.Generic;
using UnityEngine;

public class BagInstance : StorageWearableInstance
{
    public BagSO BagData => ItemSO as BagSO;

    public BagInstance(ItemSO data) : base(data)
    {
    }

    public void InitializeBagCapacity(int customCapacity = 0)
    {
        if (BagData != null)
        {
            int finalCapacity = (customCapacity > 0) ? customCapacity : BagData.Capacity;
            InitializeStorage(finalCapacity);
        }
    }
}