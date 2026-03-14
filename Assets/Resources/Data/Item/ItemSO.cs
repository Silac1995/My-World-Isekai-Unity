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
    [SerializeField] private ItemWeight _weight = ItemWeight.Medium;

    public string ItemId => itemId;
    public string ItemName => itemName;
    public string Description => description;
    public Sprite Icon => icon;
    public string CategoryName => category_name;
    public GameObject ItemPrefab => item_prefab;
    public GameObject WorldItemPrefab => worldItem_prefab;
    public SpriteLibraryAsset SpriteLibraryAsset => _spriteLibrary;
    public ItemWeight Weight => _weight;
    public abstract System.Type InstanceType { get; }

    [Header("Crafting Requirements")]
    [SerializeField] private SkillSO _requiredCraftingSkill;
    [SerializeField] private int _requiredCraftingLevel = 1;
    [SerializeField] private float _craftingDuration = 3f;
    [SerializeField] private System.Collections.Generic.List<CraftingIngredient> _craftingRecipe = new System.Collections.Generic.List<CraftingIngredient>();

    public SkillSO RequiredCraftingSkill => _requiredCraftingSkill;
    public int RequiredCraftingLevel => _requiredCraftingLevel;
    public float CraftingDuration => _craftingDuration;
    public System.Collections.Generic.List<CraftingIngredient> CraftingRecipe => _craftingRecipe;

    // On change 'virtual' en 'abstract' et on retire le corps de la méthode
    public abstract ItemInstance CreateInstance();
}

[System.Serializable]
public struct CraftingIngredient
{
    public ItemSO Item;
    public int Amount;
}
