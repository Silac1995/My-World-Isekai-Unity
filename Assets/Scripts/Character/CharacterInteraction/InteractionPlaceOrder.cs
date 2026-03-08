using UnityEngine;
using System.Linq;

/// <summary>
/// Action d'interaction permettant à un personnage (ou joueur) de valider une commande (BuyOrder)
/// auprès d'un PNJ possédant le rôle de JobLogisticsManager.
/// </summary>
public class InteractionPlaceOrder : ICharacterInteractionAction
{
    private BuyOrder _order;

    public InteractionPlaceOrder(BuyOrder order)
    {
        _order = order;
    }

    public void Execute(Character source, Character target)
    {
        if (_order == null) return;

        Debug.Log($"<color=lightblue>[Order]</color> {source.CharacterName} demande à passer une commande logistique auprès de {target.CharacterName}...");

        if (target.CharacterJob != null)
        {
            var manager = target.CharacterJob.ActiveJobs
                .Select(j => j.AssignedJob as JobLogisticsManager)
                .FirstOrDefault(j => j != null);

            if (manager != null)
            {
                if (manager.PlaceBuyOrder(_order))
                {
                    Debug.Log($"<color=green>[Order]</color> Commande de {_order.Quantity}x {_order.ItemToTransport.ItemName} acceptée par le Manager {target.CharacterName}.");
                    
                    // Boost mineur de relation pour un accord commercial
                    if (source.CharacterRelation != null) source.CharacterRelation.UpdateRelation(target, 2);
                    if (target.CharacterRelation != null) target.CharacterRelation.UpdateRelation(source, 2);

                    if (source.CharacterSpeech != null) source.CharacterSpeech.Say($"J'ai besoin de livrer {_order.Quantity}x {_order.ItemToTransport.ItemName}.");
                    // On ne force pas la target à répondre instantanément si on veut respecter le DialogueSequence,
                    // mais pour le prototype on peut le log ou le Say décalé.
                }
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Order]</color> {target.CharacterName} n'est pas un Manager Logistique.");
                if (target.CharacterSpeech != null) target.CharacterSpeech.Say("Euh... Je ne m'occupe pas des livraisons.");
            }
        }
    }
}
