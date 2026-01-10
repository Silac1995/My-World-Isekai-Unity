using UnityEngine;

public class Equipment
{
    public EquipmentSO Data { get; }
    protected string type = "Equipment";
    protected GameObject equipment;

    public GameObject EquipmentObject
    {
        get => equipment;
        set => equipment = value;
    }
}
