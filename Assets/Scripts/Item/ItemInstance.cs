using UnityEngine;

[System.Serializable]
public class ItemInstance
{
    [SerializeField] private ItemSO _itemSO;
    [SerializeField] private string _customizedName;

    // Couleurs personnalisables (Initialisées à transparent = None)
    [SerializeField] private Color _primaryColor = new Color(0, 0, 0, 0);
    [SerializeField] private Color _secondaryColor = new Color(0, 0, 0, 0);

    public ItemInstance(ItemSO data)
    {
        _itemSO = data;
        if (data != null) _customizedName = data.ItemName;
    }

    // --- Getters & Setters ---

    public ItemSO ItemSO { get => _itemSO; set => _itemSO = value; }
    public string CustomizedName { get => _customizedName; set => _customizedName = value; }

    public Color PrimaryColor => _primaryColor;
    public Color SecondaryColor => _secondaryColor;

    // --- Méthodes de Gestion des Couleurs ---

    public void SetPrimaryColor(Color newColor)
    {
        _primaryColor = newColor;
        // Si on applique une couleur, on s'assure qu'elle est opaque (visible)
        if (_primaryColor.a <= 0f) _primaryColor.a = 1f;
    }

    public void SetSecondaryColor(Color newColor)
    {
        _secondaryColor = newColor;
        if (_secondaryColor.a <= 0f) _secondaryColor.a = 1f;
    }

    /// <summary>
    /// Réinitialise les couleurs à "None" (transparent)
    /// </summary>
    public void ResetColors()
    {
        _primaryColor = new Color(0, 0, 0, 0);
        _secondaryColor = new Color(0, 0, 0, 0);
    }

    // --- Vérifications (Checkers) ---

    public bool HavePrimaryColor() => _primaryColor.a > 0f;

    public bool HaveSecondaryColor() => _secondaryColor.a > 0f;

    public bool HaveCustomizedName() => !string.IsNullOrWhiteSpace(_customizedName);

    // --- Initialisation ---

    public void InitializeItem(ItemSO newItem)
    {
        _itemSO = newItem;
        if (_itemSO != null)
        {
            _customizedName = _itemSO.ItemName;
        }
    }
}