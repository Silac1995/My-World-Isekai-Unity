using System.Collections.Generic;
using UnityEngine;
using MWI.Time;

/// <summary>
/// Gère la réception et la distribution des commandes (BuyOrders) pour un bâtiment.
/// Inclus dans TransporterBuilding et les bâtiments nécessitant des expéditions (ex: GatheringBuilding).
/// </summary>
public class JobLogisticsManager : Job
{
    private string _customTitle;
    public override string JobTitle => _customTitle;
    public override JobCategory Category => JobCategory.Service;

    // Liste des commandes à traiter
    private List<BuyOrder> _activeOrders = new List<BuyOrder>();
    public IReadOnlyList<BuyOrder> ActiveOrders => _activeOrders;

    // Liste des commandes de fabrication (Crafting) locales au bâtiment
    private List<CraftingOrder> _activeCraftingOrders = new List<CraftingOrder>();
    public IReadOnlyList<CraftingOrder> ActiveCraftingOrders => _activeCraftingOrders;

    public JobLogisticsManager(string title = "Logistics Manager")
    {
        _customTitle = title;

        // On s'abonne au changement de jour pour vérifier l'expiration des commandes
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += CheckExpiredOrders;
        }
    }

    ~JobLogisticsManager()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= CheckExpiredOrders;
        }
    }

    /// <summary>
    /// Utilisé (notamment via UI ou Interactions) pour déposer une commande auprès de ce manager.
    /// </summary>
    public bool PlaceBuyOrder(BuyOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;

        _activeOrders.Add(order);
        Debug.Log($"<color=cyan>[JobLogisticsManager]</color> Commande reçue : {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Destination.BuildingName}. Jours restants : {order.RemainingDays}");
        return true;
    }

    /// <summary>
    /// Dépôt d'une commande de fabrication (locale).
    /// </summary>
    public bool PlaceCraftingOrder(CraftingOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;

        _activeCraftingOrders.Add(order);
        Debug.Log($"<color=cyan>[JobLogisticsManager]</color> Commande Craft reçue : {order.Quantity}x {order.ItemToCraft.ItemName}. Jours restants : {order.RemainingDays}");
        return true;
    }

    /// <summary>
    /// Utilisé par les JobTransporter pour récupérer une commande à effectuer.
    /// Retourne la commande dont la deadline est la plus proche, ou la première de la liste.
    /// </summary>
    public BuyOrder GetNextAvailableOrder()
    {
        if (_activeOrders.Count == 0) return null;

        // Trie optionnel : prendre la commande avec le plus petit Délai restants (RemainingDays)
        BuyOrder nextOrder = _activeOrders[0];
        foreach(var order in _activeOrders)
        {
            if(order.RemainingDays < nextOrder.RemainingDays)
            {
                nextOrder = order;
            }
        }
        return nextOrder;
    }

    /// <summary>
    /// Utilisé par les JobCrafter pour récupérer une commande de fabrication à effectuer.
    /// Retourne la commande dont la deadline est la plus proche, ou la première de la liste.
    /// </summary>
    public CraftingOrder GetNextAvailableCraftingOrder()
    {
        if (_activeCraftingOrders.Count == 0) return null;

        CraftingOrder nextOrder = _activeCraftingOrders[0];
        foreach(var order in _activeCraftingOrders)
        {
            if(order.RemainingDays < nextOrder.RemainingDays)
            {
                nextOrder = order;
            }
        }
        return nextOrder;
    }

    /// <summary>
    /// Met à jour la progression d'une commande.
    /// Si complétée, elle est retirée de la liste.
    /// </summary>
    public void UpdateOrderProgress(BuyOrder order, int deliveredAmount)
    {
        if (!_activeOrders.Contains(order)) return;

        bool completed = order.RecordDelivery(deliveredAmount);
        if (completed)
        {
            _activeOrders.Remove(order);
            Debug.Log($"<color=green>[JobLogisticsManager]</color> Commande {order.Quantity}x {order.ItemToTransport.ItemName} COMPLÉTÉE.");
        }
    }

    /// <summary>
    /// Met à jour la progression d'une commande de fabrication.
    /// Si complétée, elle est retirée de la liste.
    /// </summary>
    public void UpdateCraftingOrderProgress(CraftingOrder order, int craftedAmount)
    {
        if (!_activeCraftingOrders.Contains(order)) return;

        bool completed = order.RecordCraft(craftedAmount);
        if (completed)
        {
            _activeCraftingOrders.Remove(order);
            Debug.Log($"<color=green>[JobLogisticsManager]</color> Commande Craft {order.Quantity}x {order.ItemToCraft.ItemName} COMPLÉTÉE.");
        }
    }

    /// <summary>
    /// Vérifie si l'une des commandes est expirée et applique les conséquences sociales.
    /// </summary>
    private void CheckExpiredOrders()
    {
        if (TimeManager.Instance == null) return;

        CheckExpiredBuyOrders();
        CheckExpiredCraftingOrders();
    }

    private void CheckExpiredBuyOrders()
    {
        if (_activeOrders.Count == 0) return;

        List<BuyOrder> expiredOrders = new List<BuyOrder>();

        foreach (var order in _activeOrders)
        {
            order.DecreaseRemainingDays();
            if (order.RemainingDays <= 0)
            {
                expiredOrders.Add(order);
            }
        }

        foreach (var expired in expiredOrders)
        {
            _activeOrders.Remove(expired);
            Debug.Log($"<color=red>[JobLogisticsManager]</color> Commande {expired.Quantity}x {expired.ItemToTransport.ItemName} EXPIRÉE.");

            // Appliquer les conséquences sociales si le bâtiment et boss sont configurés
            if (_workplace != null && _workplace.Owner != null)
            {
                Character transporterBoss = _workplace.Owner;

                if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                {
                    // Le patron qui n'a pas reçu sa commande déteste le boss du transporteur
                    expired.ClientBoss.CharacterRelation?.UpdateRelation(transporterBoss, -25);
                }

                if (expired.IntermediaryBoss != null && expired.IntermediaryBoss.IsAlive())
                {
                    // Le patron qui a payé le transport mais que ça n'a pas été livré
                    expired.IntermediaryBoss.CharacterRelation?.UpdateRelation(transporterBoss, -10);
                }
            }
        }
    }

    private void CheckExpiredCraftingOrders()
    {
        if (_activeCraftingOrders.Count == 0) return;

        List<CraftingOrder> expiredOrders = new List<CraftingOrder>();

        foreach (var order in _activeCraftingOrders)
        {
            order.DecreaseRemainingDays();
            if (order.RemainingDays <= 0)
            {
                expiredOrders.Add(order);
            }
        }

        foreach (var expired in expiredOrders)
        {
            _activeCraftingOrders.Remove(expired);
            Debug.Log($"<color=red>[JobLogisticsManager]</color> Commande Craft {expired.Quantity}x {expired.ItemToCraft.ItemName} EXPIRÉE.");

            // Appliquer les conséquences sociales si le bâtiment et boss sont configurés
            if (_workplace != null && _workplace.Owner != null)
            {
                Character workplaceBoss = _workplace.Owner;

                if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                {
                    // Le patron qui n'a pas reçu sa commande déteste le boss de l'artisan
                    expired.ClientBoss.CharacterRelation?.UpdateRelation(workplaceBoss, -25);
                }
            }
        }
    }

    public override void Execute()
    {
        // Le Manager peut rester à un bureau (Behaviour Tree ou GOAP).
        // Logique vide pour l'instant car c'est un métier principalement piloté par UI et Interactions.
    }

    public override string CurrentActionName => "Managing Orders";
}
