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
    
    // L'item actuellement tenu (utilisé par GOAP)
    public ItemInstance CarriedItem { get; private set; }

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
            CarriedItem = null;
            
            // Forcer la replanification GOAP
            if (_currentAction != null)
            {
                _currentAction.Exit(_worker);
                _currentAction = null;
            }
            _currentPlan = null;
        }
    }

    public void SetCarriedItem(ItemInstance item)
    {
        CarriedItem = item;
    }

    public override void Execute()
    {
        if (_worker == null) return;

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
        var worldState = new Dictionary<string, bool>
        {
            { "hasDelivered", false },
            { "isLoaded", CarriedItem != null },
            { "isIdling", false }
        };

        var availableActions = new List<GoapAction>
        {
            new GoapAction_LoadTransport(this),
            new GoapAction_UnloadTransport(this),
            new GoapAction_IdleInCommercialBuilding(_workplace as CommercialBuilding)
        };

        GoapGoal targetGoal;
        if (CurrentOrder != null)
        {
            targetGoal = new GoapGoal("DeliverItems", new Dictionary<string, bool> { { "hasDelivered", true } }, priority: 1);
        }
        else
        {
            targetGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);
        }

        _transporterGoal = targetGoal;
        
        var validActions = availableActions.Where(a => a.IsValid(_worker)).ToList();
        
        _currentPlan = GoapPlanner.Plan(worldState, validActions, targetGoal);

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
        CarriedItem = null;
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
        if (CarriedItem == null)
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

            CarriedItem = null; // Always clear the carried item once credit is given

            if (CurrentOrder.IsCompleted)
            {
                CurrentOrder = null;
                _currentPlan = null;
            }
        }
    }

    public void CancelCurrentOrder()
    {
        Debug.Log($"<color=orange>[JobTransporter]</color> {_worker?.CharacterName} annule sa livraison en cours.");
        CurrentOrder = null;
        CarriedItem = null;
        
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
