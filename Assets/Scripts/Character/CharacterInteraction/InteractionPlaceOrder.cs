using UnityEngine;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Action d'interaction permettant à un personnage de passer une commande (BuyOrder ou CraftingOrder)
/// auprès d'un PNJ possédant le rôle de JobLogisticsManager.
/// </summary>
public class InteractionPlaceOrder : ICharacterInteractionAction
{
    private List<JobLogisticsManager.PendingOrder> _pendingOrders;
    private CommercialBuilding _targetBuilding;

    public InteractionPlaceOrder(Character source, Character target, CommercialBuilding targetBuilding, List<JobLogisticsManager.PendingOrder> pendingOrders)
    {
        _pendingOrders = pendingOrders;
        _targetBuilding = targetBuilding;
    }

    public void Execute(Character source, Character target)
    {
        Debug.Log($"<color=lightblue>[Order]</color> {source.CharacterName} demande à passer une commande auprès de {target.CharacterName}...");

        if (target.CharacterJob == null) return;

        var manager = target.CharacterJob.ActiveJobs
            .Select(j => j.AssignedJob as JobLogisticsManager)
            .FirstOrDefault(j => j != null && j.Workplace == _targetBuilding);

        if (manager == null)
        {
            Debug.LogWarning($"<color=orange>[Order]</color> {target.CharacterName} n'est pas un Manager Logistique pour {_targetBuilding?.BuildingName}.");
            if (target.CharacterSpeech != null) target.CharacterSpeech.Say("Euh... Je ne m'occupe pas des commandes ici.");
            return;
        }

        foreach (var orderData in _pendingOrders)
        {
            if (orderData.Type == JobLogisticsManager.OrderType.Buy && orderData.BuyOrder != null)
            {
                ExecuteBuyOrder(source, target, manager, orderData.BuyOrder);
            }
            else if (orderData.Type == JobLogisticsManager.OrderType.Crafting && orderData.CraftingOrder != null)
            {
                ExecuteCraftingOrder(source, target, manager, orderData.CraftingOrder);
            }
            else if (orderData.Type == JobLogisticsManager.OrderType.Transport && orderData.TransportOrder != null)
            {
                ExecuteTransportOrder(source, target, manager, orderData.TransportOrder);
            }
        }
    }

    private void ExecuteBuyOrder(Character source, Character target, JobLogisticsManager manager, BuyOrder order)
    {
        if (manager.PlaceBuyOrder(order))
        {
            Debug.Log($"<color=green>[Order]</color> BuyOrder de {order.Quantity}x {order.ItemToTransport.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"J'ai besoin de {order.Quantity}x {order.ItemToTransport.ItemName}.");
        }
    }

    private void ExecuteCraftingOrder(Character source, Character target, JobLogisticsManager manager, CraftingOrder order)
    {
        if (manager.PlaceCraftingOrder(order))
        {
            Debug.Log($"<color=green>[Order]</color> CraftingOrder de {order.Quantity}x {order.ItemToCraft.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"J'ai besoin de fabriquer {order.Quantity}x {order.ItemToCraft.ItemName}.");
        }
    }

    private void ExecuteTransportOrder(Character source, Character target, JobLogisticsManager manager, TransportOrder order)
    {
        if (manager.PlaceTransportOrder(order))
        {
            Debug.Log($"<color=green>[Order]</color> TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"Pouvez-vous transporter {order.Quantity}x {order.ItemToTransport.ItemName} ?");
        }
    }
}
