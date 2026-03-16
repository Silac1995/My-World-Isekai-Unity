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

    // Quantité actuellement dans les sacs/mains des transporteurs
    public int InTransitQuantity { get; private set; }

    public bool IsCompleted => DeliveredQuantity >= Quantity;

    // Indique si la commande a été officiellement acceptée par le fournisseur via interaction
    public bool IsPlaced { get; set; } = false;

    // The physical ItemInstances explicitly reserved for this order from the source building's inventory
    public System.Collections.Generic.List<ItemInstance> ReservedItems { get; private set; } = new System.Collections.Generic.List<ItemInstance>();

    public BuyOrder AssociatedBuyOrder { get; private set; }

    public TransportOrder(ItemSO item, int quantity, CommercialBuilding source, CommercialBuilding dest, BuyOrder associatedBuyOrder = null)
    {
        ItemToTransport = item;
        Quantity = quantity;
        Source = source;
        Destination = dest;
        AssociatedBuyOrder = associatedBuyOrder;
        DeliveredQuantity = 0;
        InTransitQuantity = 0;
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

    public void AddInTransit(int amount)
    {
        InTransitQuantity += amount;
    }

    public void RemoveInTransit(int amount)
    {
        InTransitQuantity = Mathf.Max(0, InTransitQuantity - amount);
    }

    public void ReserveItem(ItemInstance item)
    {
        if (item != null && !ReservedItems.Contains(item))
        {
            ReservedItems.Add(item);
        }
    }

    public void UnreserveItem(ItemInstance item)
    {
        if (item != null && ReservedItems.Contains(item))
        {
            ReservedItems.Remove(item);
        }
    }
}
