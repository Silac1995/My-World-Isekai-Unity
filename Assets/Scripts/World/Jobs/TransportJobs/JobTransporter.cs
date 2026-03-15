using System.Linq;
using UnityEngine;

/// <summary>
/// Métier du livreur. Demande du travail à son JobLogisticsManager interne, puis part en livraison.
/// Exécute désormais le transport nativement via sa propre machine à états (MovingToSource, PickingUp, etc.).
/// </summary>
public class JobTransporter : Job
{
    private string _customTitle;
    public override string JobTitle => _customTitle;
    public override JobCategory Category => JobCategory.Transporter;

    // La commande courante que l'employé est en train de livrer
    public BuyOrder CurrentOrder { get; private set; }

    private enum TransportPhase
    {
        Idle,
        MovingToSource,
        PickingUp,
        MovingToDestination,
        DroppingOff
    }

    private TransportPhase _currentPhase = TransportPhase.Idle;
    private float _nextActionTime = 0f;
    private const float PICKUP_TIME = 2f;
    private const float DROPOFF_TIME = 2f;

    public JobTransporter(string title = "Transporter")
    {
        _customTitle = title;
    }

    public void AssignOrder(BuyOrder order)
    {
        CurrentOrder = order;
        if (order != null)
        {
            Debug.Log($"<color=green>[JobTransporter]</color> {_worker?.CharacterName} commence la demande: Livrer {order.ItemToTransport.ItemName} à {order.Destination.BuildingName}.");
            _currentPhase = TransportPhase.MovingToSource;
            _nextActionTime = 0f;
        }
    }

    public override void Execute()
    {
        if (_worker == null) return;

        // Diminution des timers
        if (Time.time < _nextActionTime)
        {
            return;
        }

        if (CurrentOrder == null)
        {
            if (HasWorkToDo())
            {
                // HasWorkToDo a assigné _currentOrder via AssignOrder
                return;
            }
            // En attente
            return;
        }

        var movement = _worker.CharacterMovement;
        if (movement == null) return;

        switch (_currentPhase)
        {
            case TransportPhase.MovingToSource:
                HandleMovementTo(CurrentOrder.Source, out bool arrivedAtSource);
                if (arrivedAtSource)
                {
                    _currentPhase = TransportPhase.PickingUp;
                    _nextActionTime = Time.time + PICKUP_TIME;
                    Debug.Log($"<color=cyan>[Transport]</color> {_worker.CharacterName} arrive au dépôt et charge {CurrentOrder.ItemToTransport.ItemName}.");
                }
                break;

            case TransportPhase.PickingUp:
                _currentPhase = TransportPhase.MovingToDestination;
                Debug.Log($"<color=cyan>[Transport]</color> {_worker.CharacterName} part livrer {CurrentOrder.ItemToTransport.ItemName} à {CurrentOrder.Destination.BuildingName}.");
                break;

            case TransportPhase.MovingToDestination:
                HandleMovementTo(CurrentOrder.Destination, out bool arrivedAtDest);
                if (arrivedAtDest)
                {
                    _currentPhase = TransportPhase.DroppingOff;
                    _nextActionTime = Time.time + DROPOFF_TIME;
                    Debug.Log($"<color=cyan>[Transport]</color> {_worker.CharacterName} arrive à la boutique et décharge {CurrentOrder.ItemToTransport.ItemName}.");
                }
                break;

            case TransportPhase.DroppingOff:
                Debug.Log($"<color=green>[Transport]</color> {_worker.CharacterName} a livré 1 lot de {CurrentOrder.ItemToTransport.ItemName} à {CurrentOrder.Destination.BuildingName} !");
                NotifyDeliveryProgress(1); // Livre 1 lot 
                
                if (CurrentOrder != null) 
                {
                    // S'il reste des trucs à livrer, on retourne à la source
                    _currentPhase = TransportPhase.MovingToSource;
                }
                else
                {
                    _currentPhase = TransportPhase.Idle;
                }
                break;
        }
    }

    private void HandleMovementTo(CommercialBuilding building, out bool arrived)
    {
        arrived = false;
        var movement = _worker.CharacterMovement;
        Vector3 targetPos = building.transform.position;
        
        if (building.BuildingZone != null)
        {
            targetPos = building.GetRandomPointInBuildingZone(_worker.transform.position.y);
        }

        float distance = Vector3.Distance(_worker.transform.position, targetPos);

        if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            if (distance > movement.StoppingDistance + 2f)
            {
                movement.SetDestination(targetPos);
            }
            else
            {
                movement.ResetPath();
                arrived = true;
            }
        }
    }

    public override bool HasWorkToDo()
    {
        if (CurrentOrder != null && !CurrentOrder.IsCompleted)
            return true;

        if (_workplace != null)
        {
            var manager = _workplace.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
            if (manager != null)
            {
                BuyOrder next = manager.GetNextAvailableOrder();
                if (next != null)
                {
                    AssignOrder(next);
                    return true;
                }
            }
        }
        return false;
    }

    public void NotifyDeliveryProgress(int amount)
    {
        if (CurrentOrder != null)
        {
            var manager = _workplace.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
            if (manager != null)
            {
                manager.UpdateOrderProgress(CurrentOrder, amount);
            }

            if (CurrentOrder.IsCompleted)
            {
                CurrentOrder = null;
                _currentPhase = TransportPhase.Idle;
            }
        }
    }

    public void CancelCurrentOrder()
    {
        Debug.Log($"<color=orange>[JobTransporter]</color> {_worker?.CharacterName} annule sa livraison en cours.");
        CurrentOrder = null;
        _currentPhase = TransportPhase.Idle;
    }

    public override string CurrentActionName => CurrentOrder != null ? $"Livraison : {CurrentOrder.ItemToTransport.ItemName}" : "En attente de commandes";
}
