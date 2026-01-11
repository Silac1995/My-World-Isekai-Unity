using UnityEngine;

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
}