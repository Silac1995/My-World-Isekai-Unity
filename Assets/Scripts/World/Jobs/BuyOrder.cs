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

    // The physical ItemInstances explicitly reserved for this order from the source building's inventory
    public System.Collections.Generic.List<ItemInstance> ReservedItems { get; private set; } = new System.Collections.Generic.List<ItemInstance>();

    /// <summary>
    /// Phase-B reachability-failure counter. Incremented each time a transporter
    /// bound to this BuyOrder aborts movement because <c>NavMesh.CalculatePath</c>
    /// returned <c>PathInvalid</c> or <c>PathPartial</c>. After <see cref="MaxPathUnreachableAttempts"/>
    /// the dispatcher flags this order as reachability-stalled so the logistics tick
    /// stops re-dispatching until it expires via <c>DecreaseRemainingDays</c>.
    /// </summary>
    public int PathUnreachableCount { get; private set; } = 0;
    public const int MaxPathUnreachableAttempts = 3;
    public bool IsReachabilityStalled => PathUnreachableCount >= MaxPathUnreachableAttempts;

    /// <summary>
    /// Bump the reachability-failure counter. Returns <c>true</c> if this increment
    /// pushed the order past <see cref="MaxPathUnreachableAttempts"/> and it should
    /// now be treated as stalled (logged + left to expire naturally).
    /// </summary>
    public bool RecordPathUnreachable()
    {
        PathUnreachableCount++;
        return IsReachabilityStalled;
    }

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

    public void AddQuantity(int amount)
    {
        if (amount > 0 && !IsPlaced)
        {
            Quantity += amount;
            Debug.Log($"<color=green>[BuyOrder]</color> Quantite augmentee de {amount}. Nouvelle quantite: {Quantity} pour {ItemToTransport.ItemName}");
        }
    }

    public void CancelDispatch(int amount)
    {
        DispatchedQuantity = Mathf.Max(0, DispatchedQuantity - amount);
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
