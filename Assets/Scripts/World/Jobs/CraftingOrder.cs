using UnityEngine;

/// <summary>
/// Représente une commande de fabrication locale au bâtiment.
/// Utilisé par le JobLogisticsManager pour transmettre les besoins de production aux artisans (JobCrafter).
/// </summary>
[System.Serializable]
public class CraftingOrder
{
    public ItemSO ItemToCraft { get; private set; }
    public int Quantity { get; private set; }
    public int RemainingDays { get; private set; }
    public Character ClientBoss { get; private set; }

    // Quantité déjà fabriquée
    public int CraftedQuantity { get; private set; }

    public bool IsCompleted => CraftedQuantity >= Quantity;

    public CraftingOrder(ItemSO item, int quantity, int remainingDays, Character clientBoss = null)
    {
        ItemToCraft = item;
        Quantity = quantity;
        RemainingDays = remainingDays;
        ClientBoss = clientBoss;
        CraftedQuantity = 0;
    }

    public void DecreaseRemainingDays()
    {
        RemainingDays--;
    }

    /// <summary>
    /// Enregistre une fabrication partielle ou totale.
    /// Retourne vrai si la commande globale est désormais complétée.
    /// </summary>
    public bool RecordCraft(int amount)
    {
        CraftedQuantity += amount;
        return IsCompleted;
    }
}
