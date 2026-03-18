using UnityEngine;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Action d'interaction permettant à un personnage de passer une commande (BuyOrder ou CraftingOrder)
/// auprès d'un PNJ possédant le rôle de JobLogisticsManager.
/// </summary>
public class InteractionPlaceOrder : ICharacterInteractionAction
{
    private List<BuildingLogisticsManager.PendingOrder> _pendingOrders;
    private CommercialBuilding _targetBuilding;

    public InteractionPlaceOrder(Character source, Character target, CommercialBuilding targetBuilding, List<BuildingLogisticsManager.PendingOrder> pendingOrders)
    {
        _pendingOrders = pendingOrders;
        _targetBuilding = targetBuilding;
    }

    public void Execute(Character source, Character target)
    {
        Debug.Log($"<color=green>[Logistics]</color> {source.CharacterName} débute l'interaction pour passer {_pendingOrders.Count} commande(s) à {target.CharacterName}.");

        if (target.CharacterJob == null) return;

        var jobManager = target.CharacterJob.ActiveJobs
            .Select(j => j.AssignedJob as JobLogisticsManager)
            .FirstOrDefault(j => j != null && j.Workplace == _targetBuilding);

        if (jobManager == null || _targetBuilding.LogisticsManager == null)
        {
            Debug.LogWarning($"<color=orange>[Order]</color> {target.CharacterName} n'est pas un Manager Logistique pour {_targetBuilding?.BuildingName}.");
            if (target.CharacterSpeech != null) target.CharacterSpeech.Say("Euh... Je ne m'occupe pas des commandes ici.");
            return;
        }
        
        var manager = _targetBuilding.LogisticsManager;

        foreach (var orderData in _pendingOrders)
        {
            if (orderData.Type == BuildingLogisticsManager.OrderType.Buy && orderData.BuyOrder != null)
            {
                ExecuteBuyOrder(source, target, manager, orderData.BuyOrder);
            }
            else if (orderData.Type == BuildingLogisticsManager.OrderType.Crafting && orderData.CraftingOrder != null)
            {
                ExecuteCraftingOrder(source, target, manager, orderData.CraftingOrder);
            }
            else if (orderData.Type == BuildingLogisticsManager.OrderType.Transport && orderData.TransportOrder != null)
            {
                ExecuteTransportOrder(source, target, manager, orderData.TransportOrder);
            }
        }
    }

    private void ExecuteBuyOrder(Character source, Character target, BuildingLogisticsManager manager, BuyOrder order)
    {
        if (manager.PlaceBuyOrder(order))
        {
            order.IsPlaced = true; // Confirme que le fournisseur a bien reçu et traité la commande
            Debug.Log($"<color=green>[Order]</color> BuyOrder de {order.Quantity}x {order.ItemToTransport.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"J'ai besoin de {order.Quantity}x {order.ItemToTransport.ItemName}.");
        }
    }

    private void ExecuteCraftingOrder(Character source, Character target, BuildingLogisticsManager manager, CraftingOrder order)
    {
        if (manager.PlaceCraftingOrder(order))
        {
            order.IsPlaced = true;
            Debug.Log($"<color=green>[Order]</color> CraftingOrder de {order.Quantity}x {order.ItemToCraft.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"J'ai besoin de fabriquer {order.Quantity}x {order.ItemToCraft.ItemName}.");
        }
    }

    private void ExecuteTransportOrder(Character source, Character target, BuildingLogisticsManager manager, TransportOrder order)
    {
        if (manager.PlaceTransportOrder(order))
        {
            order.IsPlaced = true;
            Debug.Log($"<color=green>[Order]</color> TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"Pouvez-vous transporter {order.Quantity}x {order.ItemToTransport.ItemName} ?");
        }
    }
}
