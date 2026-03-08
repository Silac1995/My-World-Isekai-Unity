using UnityEngine;

/// <summary>
/// Comportement du Vendeur.
/// Se tient derrière son comptoir et appelle les clients dans la file d'attente du magasin.
/// </summary>
public class BTVendorBehaviour : IAIBehaviour
{
    private NPCController _npc;
    private ShopBuilding _shop;
    private Character _currentClient;
    private bool _isFinished = false;

    public bool IsFinished => _isFinished;

    public BTVendorBehaviour(NPCController npc, ShopBuilding shop)
    {
        _npc = npc;
        _shop = shop;
        Debug.Log($"<color=cyan>[VendorAI]</color> {_npc.Character.CharacterName} commence son service au { _shop.BuildingName }.");
    }

    public void Act(Character character)
    {
        if (_shop == null || _isFinished) return;

        // 1. Si on a déjà un client en cours de transaction, on attend
        if (_currentClient != null)
        {
            // Vérifier si le client est parti ou a terminé l'interaction
            if (!_currentClient.IsAlive() || Vector3.Distance(character.transform.position, _currentClient.transform.position) > 4f)
            {
                _currentClient = null;
            }
            return;
        }

        // 2. Si on est libre, on vérifie la file d'attente du magasin
        if (_shop.CustomersInQueue > 0)
        {
            _currentClient = _shop.GetNextCustomer();
            
            if (_currentClient != null)
            {
                Debug.Log($"<color=cyan>[VendorAI]</color> {character.CharacterName} appelle {_currentClient.CharacterName} ({_shop.CustomersInQueue} restants dans la file).");
                
                // On déclenche l'interaction de vente
                character.CharacterInteraction.StartInteractionWith(_currentClient, new InteractionBuyItem(_shop));
            }
        }
    }

    public void Exit(Character character)
    {
        // S'il finit son service, vérifier s'il faut vider la file
        _shop.ClearQueue();
    }

    public void Terminate() => _isFinished = true;
}
