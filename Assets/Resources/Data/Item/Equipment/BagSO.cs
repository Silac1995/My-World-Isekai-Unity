using UnityEngine;

[CreateAssetMenu(fileName = "BagSO", menuName = "Scriptable Objects/BagSO")]
public class BagSO : WearableSO
{
    [SerializeField] readonly private EquipmentCategory _equipmentCategory = EquipmentCategory.Wearable;
    [SerializeField] readonly private WearableLayerEnum _equipmentLayer = WearableLayerEnum.Bag;
    [Header("Bag Configuration")]
    [SerializeField] private int capacity = 10;
    public int Capacity => capacity;

    public override System.Type InstanceType => typeof(BagInstance);

    public override ItemInstance CreateInstance()
    {
        // ERREUR POSSIBLE ICI : Assure-toi de bien créer un BagInstance et non un EquipmentInstance
        BagInstance newInstance = new BagInstance(this);

        // On initialise les slots (très important pour ton stockage !)
        newInstance.InitializeBagCapacity(this.capacity);

        return newInstance;
    }
}