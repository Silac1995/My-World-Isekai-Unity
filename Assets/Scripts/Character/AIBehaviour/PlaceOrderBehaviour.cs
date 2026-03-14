using UnityEngine;
using System.Linq;

/// <summary>
/// Behaviour qui fait se déplacer le personnage vers un fournisseur pour passer une commande physiquement.
/// </summary>
public class PlaceOrderBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private JobLogisticsManager.PendingOrder _pendingOrder;
    private bool _isFinished = false;
    private bool _isMoving = false;
    private float _checkTimer = 0f;
    private const float CHECK_INTERVAL = 1f;

    public bool IsFinished => _isFinished;

    public PlaceOrderBehaviour(NPCController npcController, JobLogisticsManager.PendingOrder pendingOrder)
    {
        _npcController = npcController;
        _pendingOrder = pendingOrder;
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        // Étape 1 : Se déplacer vers le bâtiment fournisseur
        if (!_isMoving)
        {
            var destination = _pendingOrder.Supplier.BuildingZone != null 
                ? _pendingOrder.Supplier.BuildingZone.bounds.center 
                : _pendingOrder.Supplier.transform.position;
            
            selfCharacter.CharacterMovement?.SetDestination(destination);
            _isMoving = true;
            Debug.Log($"<color=cyan>[Logistics]</color> {selfCharacter.CharacterName} se déplace vers {_pendingOrder.Supplier.BuildingName} pour passer commande.");
        }

        // Étape 2 : Vérifier si on est arrivé et si le manager cible est libre
        _checkTimer += Time.deltaTime;
        if (_checkTimer >= CHECK_INTERVAL)
        {
            _checkTimer = 0f;

            // Arrivé ? (Proximité du building)
            float dist = Vector3.Distance(selfCharacter.transform.position, _pendingOrder.Supplier.transform.position);
            if (dist < 5f) // Distance arbitraire pour "être au bâtiment"
            {
                var targetLogistics = _pendingOrder.Supplier.Jobs.OfType<JobLogisticsManager>().FirstOrDefault();
                if (targetLogistics != null && targetLogistics.Worker != null)
                {
                    var targetWorker = targetLogistics.Worker;
                    
                    // Est-ce que les deux sont libres pour l'interaction ?
                    if (selfCharacter.IsFree() && targetWorker.IsFree())
                    {
                        var interactionAction = _pendingOrder.IsCrafting 
                            ? new InteractionPlaceOrder(_pendingOrder.CraftingOrder)
                            : new InteractionPlaceOrder(_pendingOrder.BuyOrder);

                        Debug.Log($"<color=cyan>[Logistics]</color> {selfCharacter.CharacterName} démarre l'interaction avec {targetWorker.CharacterName} pour la commande.");
                        selfCharacter.CharacterInteraction?.StartInteractionWith(targetWorker, interactionAction);
                        
                        _isFinished = true;
                    }
                    else
                    {
                        // On attend que le manager soit libre
                        Debug.Log($"<color=yellow>[Logistics]</color> {selfCharacter.CharacterName} attend que {targetWorker.CharacterName} soit libre.");
                    }
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[Logistics]</color> Cible introuvable à {_pendingOrder.Supplier.BuildingName}.");
                    _isFinished = true;
                }
            }
        }
    }

    public void Exit(Character selfCharacter)
    {
        selfCharacter.CharacterMovement?.ResetPath();
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
