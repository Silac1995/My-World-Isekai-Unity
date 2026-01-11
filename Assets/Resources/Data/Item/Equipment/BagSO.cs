using UnityEngine;

[CreateAssetMenu(fileName = "BagSO", menuName = "Scriptable Objects/BagSO")]
public class BagSO : StorageWearableSO
{
    [SerializeField] readonly private EquipmentCategory _equipmentCategory = EquipmentCategory.Wearable;
    [SerializeField] readonly private WearableLayerEnum _equipmentLayer = WearableLayerEnum.Bag;

    public override System.Type InstanceType => typeof(BagInstance);

    public override ItemInstance CreateInstance()
    {
        BagInstance newInstance = new BagInstance(this);
        // On utilise Capacity qui vient de StorageWearableSO
        newInstance.InitializeBagCapacity(this.Capacity);
        return newInstance;
    }
}