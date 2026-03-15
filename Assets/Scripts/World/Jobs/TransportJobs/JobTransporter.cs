using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using MWI.AI;

/// <summary>
/// Métier du livreur. Demande du travail à son JobLogisticsManager interne, puis part en livraison.
/// Exécute désormais le transport nativement via GOAP.
/// </summary>
public class JobTransporter : Job
{
    private string _customTitle;
    public override string JobTitle => _customTitle;
    public override JobCategory Category => JobCategory.Transporter;

    // La commande courante que l'employé est en train de livrer
    public TransportOrder CurrentOrder { get; private set; }
    
    // Les items actuellement tenus (logique, utilisé par GOAP et l'inventaire)
    public List<ItemInstance> CarriedItems { get; private set; } = new List<ItemInstance>();
    
    // La référence physique de l'item ciblé au sol
    public WorldItem TargetWorldItem { get; set; }

    public bool ForceDeliverPartialBatch { get; set; } = false;
    public float WaitCooldown { get; set; } = 0f;

    // --- GOAP ---
    private GoapGoal _transporterGoal;
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

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
            CarriedItems.Clear();
            TargetWorldItem = null;
            
            // Forcer la replanification GOAP
            if (_currentAction != null)
            {
                _currentAction.Exit(_worker);
                _currentAction = null;
            }
            _currentPlan = null;
        }
    }

    public void AddCarriedItem(ItemInstance item)
    {
        if (item != null && !CarriedItems.Contains(item))
        {
            CarriedItems.Add(item);
            if (CurrentOrder != null) CurrentOrder.AddInTransit(1);
        }
    }

    public void RemoveCarriedItem(ItemInstance item)
    {
        if (CarriedItems.Contains(item))
        {
            CarriedItems.Remove(item);
            if (CarriedItems.Count == 0)
            {
                ForceDeliverPartialBatch = false;
            }
        }
    }

    public override void Execute()
    {
        if (_worker == null) return;

        if (WaitCooldown > 0)
        {
            WaitCooldown -= UnityEngine.Time.deltaTime;
            return;
        }

        // Diminution des timers / File d'attente
        if (CurrentOrder == null)
        {
            if (HasWorkToDo())
            {
                // HasWorkToDo a assigné CurrentOrder via AssignOrder
                return;
            }
        }

        // --- Exécution GOAP ---
        if (_currentAction != null)
        {
            if (!_currentAction.IsValid(_worker))
            {
                Debug.Log($"<color=orange>[JobTransporter]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification...");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                Debug.Log($"<color=cyan>[JobTransporter]</color> {_worker.CharacterName} : action {_currentAction.ActionName} terminée.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                // Si on a d'autres actions dans le plan, on prend la suivante,
                // sinon on met le plan à null pour forcer un nouveau cycle.
                if (_currentPlan != null && _currentPlan.Count > 0)
                {
                    _currentAction = _currentPlan.Dequeue();
                    Debug.Log($"<color=green>[JobTransporter]</color> {_worker.CharacterName} : passe à l'action suivante → {_currentAction.ActionName}");
                }
                else
                {
                    _currentPlan = null;
                }
            }
            return;
        }

        PlanNextActions();
    }

    private void PlanNextActions()
    {
        bool itemLocated = TargetWorldItem != null;
        bool atItem = false;
        
        bool atSourceStorage = false;
        
        bool itemCarried = false;
        if (CurrentOrder != null)
        {
            var workerPos = _worker.transform.position;
            var charCollider = _worker.Collider;
            
            // Check if at source storage
            CommercialBuilding source = CurrentOrder.Source;
            if (source != null)
            {
                Zone targetZone = source.StorageZone ?? source.MainRoom.GetComponent<Zone>();
                if (targetZone != null)
                {
                    var zoneCol = targetZone.GetComponent<Collider>();
                    if (zoneCol != null && charCollider != null && zoneCol.bounds.Intersects(charCollider.bounds))
                    {
                        atSourceStorage = true;
                    }
                    else if (zoneCol != null && zoneCol.bounds.Contains(workerPos))
                    {
                        atSourceStorage = true;
                    }
                }
            }

            if (CarriedItems.Count > 0)
            {
                int remainingNeeded = CurrentOrder.Quantity - CurrentOrder.DeliveredQuantity;
                bool hasEnough = CarriedItems.Count >= remainingNeeded;
                
                bool canCarryMore = false;
                if (_worker.CharacterEquipment != null)
                {
                    if (_worker.CharacterEquipment.HasFreeSpaceForItemSO(CurrentOrder.ItemToTransport))
                    {
                        canCarryMore = true;
                    }
                    else
                    {
                        var hands = _worker.CharacterVisual?.BodyPartsController?.HandsController;
                        if (hands != null && hands.AreHandsFree()) canCarryMore = true;
                    }
                }
                
                // Si on a assez d'objets pour finir l'ordre, OU si on ne peut physiquement plus en porter un seul
                // Ou qu'on est forcé (ForceDeliverPartialBatch) de livrer car aucune autre dispo
                if (hasEnough || !canCarryMore || ForceDeliverPartialBatch)
                {
                    itemCarried = true;
                    // Important: clear TargetWorldItem so we don't hold a phantom reference while delivering
                    TargetWorldItem = null;
                    itemLocated = false;
                }
            }
        }

        bool atDestination = false;
        bool itemDelivered = false;

        if (CurrentOrder != null)
        {
            var workerPos = _worker.transform.position;
            var charCollider = _worker.GetComponent<Collider>();
            
            // Check if at item
            if (itemLocated && TargetWorldItem.ItemInteractable != null && TargetWorldItem.ItemInteractable.InteractionZone != null)
            {
                if (charCollider != null && TargetWorldItem.ItemInteractable.InteractionZone.bounds.Intersects(charCollider.bounds))
                {
                    atItem = true;
                }
                else if (TargetWorldItem.ItemInteractable.InteractionZone.bounds.Contains(workerPos))
                {
                    atItem = true;
                }
            }

            // Check if at destination
            if (CurrentOrder.Destination != null)
            {
                Zone deliveryZone = CurrentOrder.Destination.DeliveryZone ?? CurrentOrder.Destination.MainRoom.GetComponent<Zone>();
                if (deliveryZone != null && deliveryZone.GetComponent<Collider>().bounds.Contains(workerPos))
                {
                    atDestination = true;
                }
                else if (deliveryZone == null && Vector3.Distance(workerPos, CurrentOrder.Destination.transform.position) < 3f)
                {
                    atDestination = true;
                }
            }
        }

        var worldState = new Dictionary<string, bool>
        {
            { "atSourceStorage", atSourceStorage },
            { "itemLocated", itemLocated },
            { "atItem", atItem },
            { "itemCarried", itemCarried },
            { "atDestination", atDestination },
            { "itemDelivered", itemDelivered },
            { "isIdling", false }
        };

        var availableActions = new List<GoapAction>
        {
            new GoapAction_GoToSourceStorage(this),
            new GoapAction_LocateItem(this),
            new GoapAction_MoveToItem(this),
            new GoapAction_PickupItem(this),
            new GoapAction_MoveToDestination(this),
            new GoapAction_DeliverItem(this),
            new GoapAction_IdleInCommercialBuilding(_workplace as CommercialBuilding)
        };

        GoapGoal targetGoal;
        if (CurrentOrder != null)
        {
            targetGoal = new GoapGoal("DeliverItems", new Dictionary<string, bool> { { "itemDelivered", true } }, priority: 1);
        }
        else
        {
            targetGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);
        }

        _transporterGoal = targetGoal;
        
        _currentPlan = GoapPlanner.Plan(worldState, availableActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            Debug.Log($"<color=green>[JobTransporter]</color> {_worker.CharacterName} : nouveau plan GOAP ! Première action → {_currentAction.ActionName}");
        }
        else
        {
            Debug.Log($"<color=orange>[JobTransporter]</color> {_worker.CharacterName} : impossible de planifier pour l'objectif {targetGoal.GoalName}.");
        }
    }

    public override void OnWorkerPunchOut()
    {
        base.OnWorkerPunchOut();
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;
    }

    public override void Unassign()
    {
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;
        CarriedItems.Clear();
        TargetWorldItem = null;
        CurrentOrder = null;

        base.Unassign();
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
        if (CarriedItems.Count == 0)
        {
            Debug.LogWarning($"<color=red>[JobTransporter]</color> {_worker?.CharacterName} a tenté d'enregistrer une livraison sans objet! Opération ignorée.");
            return;
        }

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
                _currentPlan = null;
                CarriedItems.Clear(); 
            }
        }
    }

    public void CancelCurrentOrder()
    {
        Debug.Log($"<color=orange>[JobTransporter]</color> {_worker?.CharacterName} annule sa livraison en cours.");
        CurrentOrder = null;
        CarriedItems.Clear();
        TargetWorldItem = null;
        
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;
    }

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "En attente de commandes";
    public override string CurrentGoalName => _transporterGoal != null ? _transporterGoal.GoalName : "No Goal";
}
