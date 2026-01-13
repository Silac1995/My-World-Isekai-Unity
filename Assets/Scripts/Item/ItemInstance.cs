using UnityEngine;
using UnityEngine.U2D.Animation;

[System.Serializable]
public abstract class ItemInstance
{
    [SerializeField] protected ItemSO _itemSO; // Changé en protected
    [SerializeField] protected string _customizedName;

    [SerializeField] private Color _primaryColor = new Color(0, 0, 0, 0);
    [SerializeField] private Color _secondaryColor = new Color(0, 0, 0, 0);

    // Constructeur protégé car la classe est abstraite
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

        Transform visualTransform = instantiatedObject.transform.Find("Visual");
        if (visualTransform == null) return;

        string[] visualParts = { "Line", "Color_Main", "Color_Primary", "Color_Secondary" };

        foreach (string partName in visualParts)
        {
            Transform part = visualTransform.Find(partName);
            if (part == null) continue;

            // 1. On s'assure que la library est à jour (si présente sur l'enfant)
            if (part.TryGetComponent(out SpriteLibrary library))
            {
                library.spriteLibraryAsset = _itemSO.SpriteLibraryAsset;
            }

            // 2. FORCE la catégorie avec celle du ScriptableObject
            if (part.TryGetComponent(out SpriteResolver resolver))
            {
                string currentLabel = resolver.GetLabel();
                // On ré-applique explicitement la catégorie du SO
                resolver.SetCategoryAndLabel(_itemSO.CategoryName, currentLabel);
            }

            // 3. Couleurs
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
        if (visualTransform == null) return;

        // On définit nos parties et on s'en sert aussi comme Labels
        string[] visualParts = { "Line", "Color_Main", "Color_Primary", "Color_Secondary" };

        foreach (string partName in visualParts)
        {
            Transform part = visualTransform.Find(partName);
            if (part == null) continue;

            // 1. Initialisation de la Library
            if (part.TryGetComponent(out SpriteLibrary library))
            {
                library.spriteLibraryAsset = _itemSO.SpriteLibraryAsset;
            }

            // 2. Initialisation du Resolver (Catégorie ET Label)
            if (part.TryGetComponent(out SpriteResolver resolver))
            {
                // On utilise partName ("Line", "Color_Main", etc.) comme Label
                // car c'est ainsi que tes sprites sont nommés dans ta Sprite Library Asset
                resolver.SetCategoryAndLabel(_itemSO.CategoryName, partName);

                // On force la mise à jour visuelle immédiate
                resolver.ResolveSpriteToSpriteRenderer();
            }

            // 3. Application des couleurs
            if (part.TryGetComponent(out SpriteRenderer sRenderer))
            {
                if (partName == "Color_Primary" && HavePrimaryColor())
                    sRenderer.color = PrimaryColor;
                else if (partName == "Color_Secondary" && HaveSecondaryColor())
                    sRenderer.color = SecondaryColor;
            }
        }
    }
}