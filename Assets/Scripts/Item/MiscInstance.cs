using UnityEngine;
[System.Serializable]
public class MiscInstance : ItemInstance
{
    // On peut imaginer que MiscSO hérite directement de ItemSO
    public MiscInstance(ItemSO data) : base(data)
    {
    }
}