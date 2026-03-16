using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Action GOAP for JobLogisticsManager.
/// Handles navigating to a supplier and directly transferring order data 
/// purely in code (without going through CharacterInteraction to avoid blocking).
/// </summary>
public class GoapAction_PlaceOrder : GoapAction
{
    public override string ActionName => "Place Order";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasPendingOrders", true }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasPendingOrders", false }
    };

    public override float Cost => 2f;

    private JobLogisticsManager _manager;
    private bool _isComplete = false;
    private bool _isMoving = false;

    public override bool IsComplete => _isComplete;

    public GoapAction_PlaceOrder(JobLogisticsManager manager)
    {
        _manager = manager;
    }

    public override bool IsValid(Character worker)
    {
        return _manager != null && _manager.HasPendingOrders && !_isComplete;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (!_manager.HasPendingOrders)
        {
            _isComplete = true;
            return;
        }

        var pendingOrder = _manager.PeekPendingOrder();
        var targetBuilding = pendingOrder.TargetBuilding;

        if (targetBuilding == null)
        {
            _manager.DequeuePendingOrder();
            return; 
        }

        var targetLogistics = targetBuilding.Jobs.OfType<JobLogisticsManager>().FirstOrDefault();
        if (targetLogistics == null || targetLogistics.Worker == null)
        {
            Debug.LogWarning($"<color=orange>[Logistics]</color> {targetBuilding.BuildingName} n'a pas de manager pour recevoir la commande.");
            _manager.DequeuePendingOrder(); 
            return; 
        }

        var targetWorker = targetLogistics.Worker;
        var movement = worker.CharacterMovement;

        if (movement == null) return;

        // On calcule la distance au sol uniquement
        Vector3 targetPos = targetWorker.transform.position;
        Vector3 currentPos = worker.transform.position;
        currentPos.y = 0;
        targetPos.y = 0;
        
        float distance = Vector3.Distance(currentPos, targetPos);

        // 1. Déplacement vers la cible
        if (distance > 3.0f)
        {
            // Si on ne marchait pas, ou s'il a perdu son path, ou si on vient de reprendre
            if (!_isMoving || movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending))
            {
                movement.SetDestination(targetWorker.transform.position);
                _isMoving = true;
            }
            return;
        }

        // 2. On est arrivé près de la cible.
        if (_isMoving)
        {
            movement.Stop();
            _isMoving = false;
        }

        // 3. Attendre que la cible soit disponible (pas endormie, etc.)
        if (!targetWorker.IsFree())
        {
            Debug.Log($"<color=yellow>[Logistics]</color> {worker.CharacterName} attend que {targetWorker.CharacterName} soit libre pour passer commande.");
            return; 
        }

        // 4. Exécuter le transfert de la commande (Business Logic)
        ExecuteOrderTransfer(worker, targetWorker, targetLogistics, pendingOrder);
    }

    private void ExecuteOrderTransfer(Character worker, Character targetWorker, JobLogisticsManager targetLogistics, JobLogisticsManager.PendingOrder firstOrder)
    {
        // Extraire TOUTES les commandes en attente pour CE bâtiment spécifique
        List<JobLogisticsManager.PendingOrder> ordersForTarget = new List<JobLogisticsManager.PendingOrder>();
        List<JobLogisticsManager.PendingOrder> remainingOrders = new List<JobLogisticsManager.PendingOrder>();

        while (_manager.HasPendingOrders)
        {
            var pOrder = _manager.PeekPendingOrder();
            _manager.DequeuePendingOrder();

            if (pOrder.TargetBuilding == firstOrder.TargetBuilding)
            {
                ordersForTarget.Add(pOrder);
            }
            else
            {
                remainingOrders.Add(pOrder);
            }
        }

        // Remettre les commandes qui n'étaient pas pour ce bâtiment
        foreach(var remaining in remainingOrders)
        {
            _manager.EnqueuePendingOrder(remaining);
        }

        // Visuals (Face-à-face)
        worker.CharacterVisual?.FaceCharacter(targetWorker);
        targetWorker.CharacterVisual?.FaceCharacter(worker);

        // Au lieu de transférer magiquement les données, on lance une interaction formelle.
        // Cela respecte l'architecture dictée par les skills job_system et logistics_cycle.
        InteractionPlaceOrder interaction = new InteractionPlaceOrder(worker, targetWorker, targetLogistics.Workplace, ordersForTarget);
        
        // On demande au CharacterInteraction d'exécuter cette interaction
        if (worker.CharacterInteraction != null)
        {
            if (worker.CharacterInteraction.StartInteractionWith(targetWorker, interaction))
            {
                Debug.Log($"<color=green>[Logistics]</color> {worker.CharacterName} débute l'interaction pour passer {ordersForTarget.Count} commande(s) à {targetWorker.CharacterName}.");
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Logistics]</color> L'interaction PlaceOrder a échoué à démarrer. Remise en file de {ordersForTarget.Count} commande(s).");
                foreach(var failOrder in ordersForTarget)
                {
                    _manager.EnqueuePendingOrder(failOrder);
                }
            }
        }
        else
        {
            Debug.LogError($"<color=red>[Logistics]</color> {worker.CharacterName} n'a pas de composant CharacterInteraction !");
        }

        _isComplete = true; // Permet au Planner de passer à l'action suivante (ou de terminer ProcessOrders)
    }

    private void UpdateRelationships(Character worker, Character targetWorker)
    {
        if (worker.CharacterRelation != null) worker.CharacterRelation.UpdateRelation(targetWorker, 2);
        if (targetWorker.CharacterRelation != null) targetWorker.CharacterRelation.UpdateRelation(worker, 2);
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        worker.CharacterMovement?.Resume();
    }
}
