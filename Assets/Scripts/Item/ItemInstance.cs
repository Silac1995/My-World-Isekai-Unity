using UnityEngine;
using UnityEngine.U2D.Animation;

[System.Serializable]
public abstract class ItemInstance
{
    [SerializeField] protected ItemSO _itemSO;
    [SerializeField] protected string _customizedName;
    public bool IsNewlyAdded { get; set; } = false;

    [SerializeField] private Color _primaryColor = new Color(0, 0, 0, 0);
    [SerializeField] private Color _secondaryColor = new Color(0, 0, 0, 0);

    protected ItemInstance(ItemSO data)
    {
        _itemSO = data;
        if (data != null) _customizedName = data.ItemName;
    }

    public ItemSO ItemSO { get => _itemSO; set => _itemSO = value; }
    public string CustomizedName { get => _customizedName; set => _customizedName = value; }
    public Color PrimaryColor => _primaryColor;
    public Color SecondaryColor => _secondaryColor;
    public GameObject ItemPrefab => ItemSO.ItemPrefab;

    public void SetPrimaryColor(Color newColor)
    {
        _primaryColor = newColor;
        if (_primaryColor.a <= 0f) _primaryColor.a = 1f;
    }

    public void SetSecondaryColor(Color newColor)
    {
        _secondaryColor = newColor;
        if (_secondaryColor.a <= 0f) _secondaryColor.a = 1f;
    }

    public bool HavePrimaryColor() => _primaryColor.a > 0f;
    public bool HaveSecondaryColor() => _secondaryColor.a > 0f;

    public void InitializePrefab(GameObject instantiatedObject)
    {
        if (instantiatedObject == null || _itemSO == null) return;

        // On cherche le conteneur Visual
        Transform visualTransform = instantiatedObject.transform.Find("Visual");
        if (visualTransform == null) visualTransform = instantiatedObject.transform;

        string[] visualParts = { "Line", "Color_Main", "Color_Primary", "Color_Secondary" };

        foreach (string partName in visualParts)
        {
            Transform part = visualTransform.Find(partName);
            if (part == null) continue;

            if (part.TryGetComponent(out SpriteLibrary library))
            {
                library.spriteLibraryAsset = _itemSO.SpriteLibraryAsset;
            }

            if (part.TryGetComponent(out SpriteResolver resolver))
            {
                string currentLabel = resolver.GetLabel();
                resolver.SetCategoryAndLabel(_itemSO.CategoryName, currentLabel);
            }

            if (part.TryGetComponent(out SpriteRenderer sRenderer))
            {
                if (partName == "Color_Primary" && HavePrimaryColor())
                    sRenderer.color = _primaryColor;
                else if (partName == "Color_Secondary" && HaveSecondaryColor())
                    sRenderer.color = _secondaryColor;
            }
        }
    }

    public void InitializeWorldPrefab(GameObject worldObject)
    {
        if (worldObject == null || _itemSO == null) return;

        Transform visualTransform = worldObject.transform.Find("Visual");
        if (visualTransform == null) visualTransform = worldObject.transform;

        string[] visualParts = { "Line", "Color_Main", "Color_Primary", "Color_Secondary" };

        foreach (string partName in visualParts)
        {
            Transform part = visualTransform.Find(partName);
            if (part == null) continue;

            if (part.TryGetComponent(out SpriteLibrary library))
            {
                library.spriteLibraryAsset = _itemSO.SpriteLibraryAsset;
            }

            if (part.TryGetComponent(out SpriteResolver resolver))
            {
                resolver.SetCategoryAndLabel(_itemSO.CategoryName, partName);
                resolver.ResolveSpriteToSpriteRenderer();
            }

            if (part.TryGetComponent(out SpriteRenderer sRenderer))
            {
                if (partName == "Color_Primary" && HavePrimaryColor())
                    sRenderer.color = _primaryColor;
                else if (partName == "Color_Secondary" && HaveSecondaryColor())
                    sRenderer.color = _secondaryColor;
            }
        }
    }


}
