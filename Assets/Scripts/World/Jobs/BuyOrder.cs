using UnityEngine;

/// <summary>
/// Représente une commande logistique (achat/transport) entre deux bâtiments.
/// Utilisé par les JobLogisticsManager et JobTransporter pour gérer l'économie.
/// </summary>
[System.Serializable]
public class BuyOrder
{
    public ItemSO ItemToTransport { get; private set; }
    public int Quantity { get; private set; }
    public CommercialBuilding Source { get; private set; }
    public CommercialBuilding Destination { get; private set; }
    public int RemainingDays { get; private set; }
    public Character ClientBoss { get; private set; }
    public Character IntermediaryBoss { get; private set; }

    // Quantité déjà livrée par les transporteurs
    public int DeliveredQuantity { get; private set; }

    // Quantité pour laquelle un TransportOrder a déjà été généré
    public int DispatchedQuantity { get; private set; }

    public bool IsCompleted => DeliveredQuantity >= Quantity;

    // Indique si la commande a été officiellement acceptée par le fournisseur via interaction
    public bool IsPlaced { get; set; } = false;

    public BuyOrder(ItemSO item, int quantity, CommercialBuilding source, CommercialBuilding dest, int remainingDays, Character clientBoss, Character intermediaryBoss = null)
    {
        ItemToTransport = item;
        Quantity = quantity;
        Source = source;
        Destination = dest;
        RemainingDays = remainingDays;
        ClientBoss = clientBoss;
        IntermediaryBoss = intermediaryBoss;
        DeliveredQuantity = 0;
        DispatchedQuantity = 0;
    }

    public void DecreaseRemainingDays()
    {
        RemainingDays--;
    }

    /// <summary>
    /// Enregistre une livraison partielle ou totale.
    /// Retourne vrai si la commande globale est désormais complétée.
    /// </summary>
    public bool RecordDelivery(int amount)
    {
        DeliveredQuantity += amount;
        return IsCompleted;
    }

    public void RecordDispatch(int amount)
    {
        DispatchedQuantity += amount;
    }
}
