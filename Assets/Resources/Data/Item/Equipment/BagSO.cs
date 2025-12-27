using UnityEngine;

[CreateAssetMenu(fileName = "BagSO", menuName = "Scriptable Objects/BagSO")]
public class BagSO : EquipmentSO
{
    [Header("Bag Configuration")]
    [SerializeField] private int capacity = 1;
    public int Capacity => capacity;

    // On force le type BagInstance
    public override System.Type InstanceType => typeof(BagInstance);

    public override ItemInstance CreateInstance()
    {
        return new BagInstance(this);
    }
}