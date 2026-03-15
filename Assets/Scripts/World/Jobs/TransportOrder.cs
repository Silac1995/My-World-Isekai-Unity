using UnityEngine;

/// <summary>
/// Logistics order representing a pure physical transport of items between two locations.
/// Segregated from BuyOrder to keep commercial and physical transactions independent (SRP).
/// </summary>
[System.Serializable]
public class TransportOrder
{
    public ItemSO ItemToTransport { get; private set; }
    public int Quantity { get; private set; }
    public CommercialBuilding Source { get; private set; }
    public CommercialBuilding Destination { get; private set; }

    // Quantité déjà livrée par les transporteurs
    public int DeliveredQuantity { get; private set; }

    public bool IsCompleted => DeliveredQuantity >= Quantity;

    public TransportOrder(ItemSO item, int quantity, CommercialBuilding source, CommercialBuilding dest)
    {
        ItemToTransport = item;
        Quantity = quantity;
        Source = source;
        Destination = dest;
        DeliveredQuantity = 0;
    }

    /// <summary>
    /// Enregistre une livraison partielle ou totale.
    /// Retourne vrai si le transport global est désormais complété.
    /// </summary>
    public bool RecordDelivery(int amount)
    {
        DeliveredQuantity += amount;
        return IsCompleted;
    }
}
