using UnityEngine;

[CreateAssetMenu(fileName = "ItemSO", menuName = "Scriptable Objects/ItemSO")]
public abstract class ItemSO : ScriptableObject
{
    [Header("Item Info")]
    [SerializeField] private string category_name;
    [SerializeField] private string itemId;
    [SerializeField] private string itemName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;

    public string ItemId => itemId;
    public string ItemName => itemName;
    public string Description => description;
    public Sprite Icon => icon;
    public string CategoryName => category_name;

    public abstract System.Type InstanceType { get; }

    public virtual ItemInstance CreateInstance()
    {
        return new ItemInstance(this);
    }
}
