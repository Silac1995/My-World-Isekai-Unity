using UnityEngine;
using System.Linq;

/// <summary>
/// Action d'interaction permettant à un personnage de passer une commande (BuyOrder ou CraftingOrder)
/// auprès d'un PNJ possédant le rôle de JobLogisticsManager.
/// </summary>
public class InteractionPlaceOrder : ICharacterInteractionAction
{
    private BuyOrder _buyOrder;
    private CraftingOrder _craftingOrder;

    public InteractionPlaceOrder(BuyOrder order)
    {
        _buyOrder = order;
    }

    public InteractionPlaceOrder(CraftingOrder order)
    {
        _craftingOrder = order;
    }

    public void Execute(Character source, Character target)
    {
        if (_buyOrder == null && _craftingOrder == null) return;

        Debug.Log($"<color=lightblue>[Order]</color> {source.CharacterName} demande à passer une commande auprès de {target.CharacterName}...");

        if (target.CharacterJob == null) return;

        var manager = target.CharacterJob.ActiveJobs
            .Select(j => j.AssignedJob as JobLogisticsManager)
            .FirstOrDefault(j => j != null);

        if (manager == null)
        {
            Debug.LogWarning($"<color=orange>[Order]</color> {target.CharacterName} n'est pas un Manager Logistique.");
            if (target.CharacterSpeech != null) target.CharacterSpeech.Say("Euh... Je ne m'occupe pas des commandes.");
            return;
        }

        if (_buyOrder != null)
        {
            ExecuteBuyOrder(source, target, manager);
        }
        else if (_craftingOrder != null)
        {
            ExecuteCraftingOrder(source, target, manager);
        }
    }

    private void ExecuteBuyOrder(Character source, Character target, JobLogisticsManager manager)
    {
        if (manager.PlaceBuyOrder(_buyOrder))
        {
            Debug.Log($"<color=green>[Order]</color> BuyOrder de {_buyOrder.Quantity}x {_buyOrder.ItemToTransport.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"J'ai besoin de {_buyOrder.Quantity}x {_buyOrder.ItemToTransport.ItemName}.");
        }
    }

    private void ExecuteCraftingOrder(Character source, Character target, JobLogisticsManager manager)
    {
        if (manager.PlaceCraftingOrder(_craftingOrder))
        {
            Debug.Log($"<color=green>[Order]</color> CraftingOrder de {_craftingOrder.Quantity}x {_craftingOrder.ItemToCraft.ItemName} acceptée par {target.CharacterName}.");

            if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
            if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

            if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"J'ai besoin de fabriquer {_craftingOrder.Quantity}x {_craftingOrder.ItemToCraft.ItemName}.");
        }
    }
}
