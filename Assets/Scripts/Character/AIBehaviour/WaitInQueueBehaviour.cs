using UnityEngine;

/// <summary>
/// Comportement du client (Customer) attendant dans la file du magasin.
/// </summary>
public class WaitInQueueBehaviour : IAIBehaviour
{
    private NPCController _npc;
    private ShopBuilding _shop;
    private ItemSO _desiredItem;
    private bool _isFinished = false;

    public bool IsFinished => _isFinished;

    public WaitInQueueBehaviour(NPCController npc, ShopBuilding shop, ItemSO desiredItem)
    {
        _npc = npc;
        _shop = shop;
        _desiredItem = desiredItem;
    }

    public void CheckStoreAvailability()
    {
        // 1. Chercher s'il y a un vendeur dispo
        JobVendor freeVendor = _shop.GetVendor();
        
        if (freeVendor != null && freeVendor.IsAssigned)
        {
            // Le vendeur est là...
        }
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character character)
    {
        if (_shop == null || _isFinished) return;

        // Si on n'est pas encore dans la file, on s'y ajoute (simulation du Start)
        // Note: On pourrait aussi le faire dans le constructeur, mais Act est appelé à chaque frame.
        // Utilisons un booléen pour s'assurer qu'on ne s'inscrit qu'une fois.
        if (!character.CharacterInteraction.IsInteracting) 
        {
             // On s'enregistre dans la file d'attente si ce n'est pas déjà fait
             _shop.JoinQueue(character);
        }
        
        // On attend simplement. C'est le Vendor qui viendra initier l'InteractionBuyItem
    }

    public void Exit(Character character)
    {
        // Si on quitte le comportement avant d'avoir acheté (faim, fermeture), on devrait s'enlever de la file
        // Note: ShopBuilding.ClearQueue() s'en charge aussi lors de la fermeture globale.
    }

    public void Terminate() => _isFinished = true;
}
