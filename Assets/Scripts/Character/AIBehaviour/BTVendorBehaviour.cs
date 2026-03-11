using UnityEngine;

/// <summary>
/// Comportement du Vendeur.
/// Se déplace vers son comptoir (VendorPoint) puis appelle les clients dans la file d'attente du magasin.
/// </summary>
public class BTVendorBehaviour : IAIBehaviour
{
    private NPCController _npc;
    private ShopBuilding _shop;
    private Character _currentClient;
    private bool _isFinished = false;
    private bool _isAtCounter = false;
    private bool _isMovingToCounter = false;

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

        // Phase 1 : Se déplacer vers le comptoir (VendorPoint)
        if (!_isAtCounter)
        {
            MoveToCounter(character);
            return;
        }

        // Phase 2 : Servir les clients

        // Si on a déjà un client en cours de transaction, on attend
        if (_currentClient != null)
        {
            // Vérifier si le client est parti ou a terminé l'interaction
            if (!_currentClient.IsAlive() || Vector3.Distance(character.transform.position, _currentClient.transform.position) > 4f)
            {
                _currentClient = null;
            }
            return;
        }

        // Si on est libre, on vérifie la file d'attente du magasin
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

    private void MoveToCounter(Character character)
    {
        var movement = character.CharacterMovement;

        if (_shop.VendorPoint == null)
        {
            // Pas de point fixe assigné → le vendeur se déplace dans la zone du bâtiment
            if (movement != null && !_isMovingToCounter)
            {
                Vector3 wanderTarget = _shop.GetRandomPointInBuildingZone(character.transform.position.y);
                movement.SetDestination(wanderTarget);
                _isMovingToCounter = true;
            }

            // Vérifier si arrivé
            if (movement != null && _isMovingToCounter 
                && !movement.PathPending 
                && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
            {
                _isAtCounter = true;
                _isMovingToCounter = false;
            }
            return;
        }

        if (movement == null)
        {
            _isAtCounter = true;
            return;
        }

        if (!_isMovingToCounter)
        {
            movement.SetDestination(_shop.VendorPoint.position);
            _isMovingToCounter = true;
            return;
        }

        // Vérifier si on est arrivé
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            _isAtCounter = true;
            _isMovingToCounter = false;
            Debug.Log($"<color=cyan>[VendorAI]</color> {character.CharacterName} est arrivé au comptoir.");
        }
    }

    public void Exit(Character character)
    {
        // S'il finit son service, vérifier s'il faut vider la file
        _shop.ClearQueue();
    }

    public void Terminate() => _isFinished = true;
}
