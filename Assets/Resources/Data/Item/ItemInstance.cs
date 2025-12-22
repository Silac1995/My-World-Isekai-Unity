using UnityEngine;

public class ItemInstance : MonoBehaviour
{
    [SerializeField] private ItemSO itemSO;
    [SerializeField] private string customizedName;

    // Cette méthode retourne 'true' si le nom n'est pas vide ou composé uniquement d'espaces
    public bool HaveCustomizedName()
    {
        return !string.IsNullOrWhiteSpace(customizedName);
    }

    // Méthode pour initialiser le ScriptableObject
    public void InitializeItem(ItemSO newItem)
    {
        itemSO = newItem;

        if (itemSO != null)
        {
            customizedName = itemSO.ItemName; // ou laisser vide selon ta logique
            // color = itemSO.defaultColor; 
        }
    }
}
