[System.Serializable]
public class WearableInstance : EquipmentInstance
{
    // Cette propriété fait le cast une fois pour toutes
    public WearableSO WearableData => ItemSO as WearableSO;

    public WearableInstance(ItemSO data) : base(data) { }
}