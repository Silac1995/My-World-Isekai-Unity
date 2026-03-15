using System.Linq;
using System.Collections.Generic;
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
    public TransportOrder CurrentOrder { get; private set; }

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
    private Vector3 _currentTargetPos = Vector3.positiveInfinity;
    private bool _actionStarted = false;
    private ItemInstance _carriedItem = null;

    public JobTransporter(string title = "Transporter")
    {
        _customTitle = title;
    }

    public void AssignOrder(TransportOrder order)
    {
        CurrentOrder = order;
        if (order != null)
        {
            Debug.Log($"<color=green>[JobTransporter]</color> {_worker?.CharacterName} commence la demande: Livrer {order.ItemToTransport.ItemName} à {order.Destination.BuildingName}.");
            _currentPhase = TransportPhase.MovingToSource;
            _currentTargetPos = Vector3.positiveInfinity;
            _actionStarted = false;
            _carriedItem = null;
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
                if (_currentTargetPos == Vector3.positiveInfinity)
                {
                    Zone zone = CurrentOrder.Source.StorageZone ?? CurrentOrder.Source.MainRoom.GetComponent<Zone>();
                    _currentTargetPos = zone != null ? zone.GetRandomPointInZone() : CurrentOrder.Source.transform.position;
                    movement.SetDestination(_currentTargetPos);
                }

                HandleMovementTo(_currentTargetPos, out bool arrivedAtSource);
                if (arrivedAtSource)
                {
                    _currentPhase = TransportPhase.PickingUp;
                    _currentTargetPos = Vector3.positiveInfinity;
                    _actionStarted = false;
                    Debug.Log($"<color=cyan>[Transport]</color> {_worker.CharacterName} arrive au dépôt et charge {CurrentOrder.ItemToTransport.ItemName}.");
                }
                break;

            case TransportPhase.PickingUp:
                if (!_actionStarted)
                {
                    _carriedItem = CurrentOrder.Source.TakeFromInventory(CurrentOrder.ItemToTransport);
                    if (_carriedItem == null) 
                    {
                        Debug.LogWarning($"<color=orange>[Transport]</color> Plus de {CurrentOrder.ItemToTransport.ItemName} disponible chez {CurrentOrder.Source.BuildingName}. Annulation.");
                        CancelCurrentOrder();
                        return;
                    }

                    var pickupAction = new CharacterPickUpItem(_worker, _carriedItem, null);
                    if (_worker.CharacterActions.ExecuteAction(pickupAction))
                    {
                        _actionStarted = true;
                        pickupAction.OnActionFinished += () => 
                        {
                            _currentPhase = TransportPhase.MovingToDestination;
                            _actionStarted = false;
                        };
                    }
                    else
                    {
                        _worker.CharacterVisual?.BodyPartsController?.HandsController?.CarryItem(_carriedItem);
                        _currentPhase = TransportPhase.MovingToDestination;
                        _actionStarted = false;
                    }
                }
                break;

            case TransportPhase.MovingToDestination:
                if (_currentTargetPos == Vector3.positiveInfinity)
                {
                    Zone zone = CurrentOrder.Destination.DeliveryZone ?? CurrentOrder.Destination.MainRoom.GetComponent<Zone>();
                    _currentTargetPos = zone != null ? zone.GetRandomPointInZone() : CurrentOrder.Destination.transform.position;
                    movement.SetDestination(_currentTargetPos);
                }

                HandleMovementTo(_currentTargetPos, out bool arrivedAtDest);
                if (arrivedAtDest)
                {
                    _currentPhase = TransportPhase.DroppingOff;
                    _currentTargetPos = Vector3.positiveInfinity;
                    _actionStarted = false;
                    Debug.Log($"<color=cyan>[Transport]</color> {_worker.CharacterName} arrive à la boutique et décharge {CurrentOrder.ItemToTransport.ItemName}.");
                }
                break;

            case TransportPhase.DroppingOff:
                if (!_actionStarted && _carriedItem != null)
                {
                    var dropAction = new CharacterDropItem(_worker, _carriedItem);
                    if (_worker.CharacterActions.ExecuteAction(dropAction))
                    {
                        _actionStarted = true;
                        dropAction.OnActionFinished += FinishDropoff;
                    }
                    else
                    {
                        var inventory = _worker.CharacterEquipment?.GetInventory();
                        if (inventory != null && inventory.HasAnyItemSO(new List<ItemSO> { _carriedItem.ItemSO }))
                            inventory.RemoveItem(_carriedItem, _worker);
                        else
                            _worker.CharacterVisual?.BodyPartsController?.HandsController?.DropCarriedItem();
                        
                        FinishDropoff();
                    }
                }
                break;
        }
    }

    private void FinishDropoff()
    {
        CurrentOrder.Destination.AddToInventory(_carriedItem);
        Debug.Log($"<color=green>[Transport]</color> {_worker.CharacterName} a livré 1 lot de {CurrentOrder.ItemToTransport.ItemName} à {CurrentOrder.Destination.BuildingName} !");
        NotifyDeliveryProgress(1);
        _carriedItem = null;
        _actionStarted = false;

        if (CurrentOrder != null) 
        {
            _currentPhase = TransportPhase.MovingToSource;
        }
        else
        {
            _currentPhase = TransportPhase.Idle;
        }
    }

    private void HandleMovementTo(Vector3 targetPos, out bool arrived)
    {
        arrived = false;
        var movement = _worker.CharacterMovement;

        float distance = Vector3.Distance(workerPosXZ(movement.transform.position), workerPosXZ(targetPos));

        if (movement.PathPending) return;

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

    private Vector3 workerPosXZ(Vector3 pos) { return new Vector3(pos.x, 0, pos.z); }

    public override bool HasWorkToDo()
    {
        if (CurrentOrder != null && !CurrentOrder.IsCompleted)
            return true;

        if (_workplace != null)
        {
            var manager = _workplace.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
            if (manager != null)
            {
                TransportOrder next = manager.GetNextAvailableTransportOrder();
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
                manager.UpdateTransportOrderProgress(CurrentOrder, amount);
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
