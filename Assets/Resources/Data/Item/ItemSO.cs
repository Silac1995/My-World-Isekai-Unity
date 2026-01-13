using UnityEngine;
using UnityEngine.U2D.Animation;

[CreateAssetMenu(fileName = "ItemSO", menuName = "Scriptable Objects/ItemSO")]
public abstract class ItemSO : ScriptableObject
{
    [Header("Item Info")]
    [SerializeField] private string category_name;
    [SerializeField] protected SpriteLibraryAsset _spriteLibrary;
    [SerializeField] private string itemId;
    [SerializeField] private string itemName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] protected GameObject item_prefab;
    [SerializeField] protected GameObject worldItem_prefab;

    public string ItemId => itemId;
    public string ItemName => itemName;
    public string Description => description;
    public Sprite Icon => icon;
    public string CategoryName => category_name;
    public GameObject ItemPrefab => item_prefab;
    public SpriteLibraryAsset SpriteLibraryAsset => _spriteLibrary;
    public abstract System.Type InstanceType { get; }

    // On change 'virtual' en 'abstract' et on retire le corps de la méthode
    public abstract ItemInstance CreateInstance();
}
