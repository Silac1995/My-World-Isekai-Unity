using UnityEngine;

/// <summary>
/// Job de Vendeur : gère les achats des clients dans un ShopBuilding.
/// Se déplace vers son comptoir (VendorPoint) puis appelle les clients dans la file d'attente du magasin.
/// </summary>
public class JobVendor : Job
{
    public override string JobTitle => "Vendeur";
    public override JobCategory Category => JobCategory.Service;

    private Character _currentClient;
    private bool _isAtCounter = false;
    private bool _isMovingToCounter = false;

    public override string CurrentActionName
    {
        get
        {
            if (!_isAtCounter) return "Moving to Counter";
            if (_currentClient != null) return $"Serving {_currentClient.CharacterName}";
            return "Waiting for Customers";
        }
    }

    public override void Execute()
    {
        if (_worker == null || !(_workplace is ShopBuilding shop)) return;

        // Phase 1 : Se déplacer vers le comptoir (VendorPoint)
        if (!_isAtCounter)
        {
            MoveToCounter(_worker, shop);
            return;
        }

        // Phase 2 : Servir les clients

        // Si on a déjà un client en cours de transaction, on attend
        if (_currentClient != null)
        {
            // Vérifier si le client est parti ou a terminé l'interaction
            if (!_currentClient.IsAlive() || Vector3.Distance(_worker.transform.position, _currentClient.transform.position) > 4f)
            {
                _currentClient = null;
            }
            return;
        }

        // Si on est libre, on vérifie la file d'attente du magasin
        if (shop.CustomersInQueue > 0)
        {
            _currentClient = shop.GetNextCustomer();
            
            if (_currentClient != null)
            {
                Debug.Log($"<color=cyan>[VendorAI]</color> {_worker.CharacterName} appelle {_currentClient.CharacterName} ({shop.CustomersInQueue} restants dans la file).");
                
                // On déclenche l'interaction de vente
                _worker.CharacterInteraction.StartInteractionWith(_currentClient, new InteractionBuyItem(shop));
            }
        }
    }

    private void MoveToCounter(Character character, ShopBuilding shop)
    {
        var movement = character.CharacterMovement;

        if (shop.VendorPoint == null)
        {
            // Pas de point fixe assigné → le vendeur se déplace dans la zone du bâtiment
            if (movement != null && !_isMovingToCounter)
            {
                Vector3 wanderTarget = shop.GetRandomPointInBuildingZone(character.transform.position.y);
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
            movement.SetDestination(shop.VendorPoint.position);
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

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is ShopBuilding;
    }

    public override void Unassign()
    {
        if (_workplace is ShopBuilding shop)
        {
            shop.ClearQueue();
        }
        _isAtCounter = false;
        _isMovingToCounter = false;
        _currentClient = null;
        base.Unassign();
    }
}
