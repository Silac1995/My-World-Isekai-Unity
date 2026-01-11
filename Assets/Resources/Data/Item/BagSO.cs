using UnityEngine;

[CreateAssetMenu(fileName = "BagSO", menuName = "Scriptable Objects/BagSO")]
public class BagSO : StorageWearableSO
{
    // On ne redéclare PAS _equipmentLayer ici !

    private void OnValidate()
    {
        // On force la valeur de la variable héritée
        _equipmentLayer = WearableLayerEnum.Bag;
    }

    public override System.Type InstanceType => typeof(BagInstance);

    public override ItemInstance CreateInstance()
    {
        BagInstance newInstance = new BagInstance(this);
        newInstance.InitializeBagCapacity(this.MiscCapacity);
        return newInstance;
    }
}