using UnityEngine;
public class ItemInstance
{
    [SerializeField] private ItemSO itemSO;
    [SerializeField] private string customizedName;
    [SerializeField] private Color customizedColor; // Par défaut : RGBA(0,0,0,0)


    public ItemInstance(ItemSO data)
    {
        this.itemSO = data;
        // On initialise le nom par défaut dès la création
        if (data != null) this.customizedName = data.ItemName;
    }
    // --- Getters & Setters ---

    public ItemSO ItemSO
    {
        get => itemSO;
        set => itemSO = value;
    }

    public string CustomizedName
    {
        get => customizedName;
        set => customizedName = value;
    }

    public Color CustomizedColor => customizedColor;

    // --- Méthodes ---

    /// <summary>
    /// Définit une couleur personnalisée pour cet item.
    /// </summary>
    /// <param name="newColor">La nouvelle couleur à appliquer.</param>
    public void SetCustomizedColor(Color newColor)
    {
        customizedColor = newColor;

        // Sécurité : si on définit une couleur, on s'assure qu'elle n'est pas transparente
        if (customizedColor.a <= 0f)
        {
            customizedColor.a = 1f;
        }
    }

    /// <summary>
    /// Vérifie si une couleur personnalisée a été définie (Alpha > 0).
    /// </summary>
    public bool HaveCustomizedColor()
    {
        return customizedColor.a > 0f;
    }

    public bool HaveCustomizedName()
    {
        return !string.IsNullOrWhiteSpace(customizedName);
    }

    public void InitializeItem(ItemSO newItem)
    {
        itemSO = newItem;

        if (itemSO != null)
        {
            CustomizedName = itemSO.ItemName;
        }
    }
}