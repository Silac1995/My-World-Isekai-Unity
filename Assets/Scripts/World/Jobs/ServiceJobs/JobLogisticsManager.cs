using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Gère la réception et la distribution des commandes (BuyOrders) pour un bâtiment.
/// Inclus dans TransporterBuilding et les bâtiments nécessitant des expéditions (ex: HarvestingBuilding).
/// La logique de données est désormais gérée par BuildingLogisticsManager.
/// </summary>
public class JobLogisticsManager : Job
{
    private string _customTitle;
    public override string JobTitle => _customTitle;
    public override JobCategory Category => JobCategory.Service;

    // GOAP
    private GoapGoal _logisticsGoal;
    private List<GoapAction> _availableActions;
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _logisticsGoal != null ? _logisticsGoal.GoalName : "No Goal";

    public JobLogisticsManager(string title = "Logistics Manager")
    {
        _customTitle = title;
    }

    public override void Assign(Character worker, CommercialBuilding workplace)
    {
        base.Assign(worker, workplace);
        Debug.Log($"<color=cyan>[Logistics]</color> {worker.CharacterName} assigné au poste logistique de {workplace.BuildingName}.");
    }

    public override void Unassign()
    {
        // Cleanup GOAP
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;

        base.Unassign();
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

    /// <summary>
    /// Appelé lorsque le worker arrive au travail (Punch In).
    /// </summary>
    public void OnWorkerPunchIn()
    {
        bool logFlow = _workplace != null
            && _workplace.LogisticsManager != null
            && _workplace.LogisticsManager.LogLogisticsFlow;

        if (!IsOwnerOrOnSchedule())
        {
            if (logFlow)
            {
                string scheduleInfo = _worker?.CharacterSchedule != null
                    ? _worker.CharacterSchedule.CurrentActivity.ToString()
                    : "no-schedule";
                Debug.Log($"<color=#ff8866>[LogisticsDBG]</color> JobLogisticsManager.OnWorkerPunchIn → EARLY EXIT. worker='{_worker?.CharacterName}', workplace='{_workplace?.BuildingName}', isOwner={(_workplace?.Owner == _worker)}, schedule={scheduleInfo}. Logistics pass skipped.");
            }
            return;
        }

        if (_workplace != null && _workplace.LogisticsManager != null)
        {
            if (logFlow)
            {
                Debug.Log($"<color=#66ccff>[LogisticsDBG]</color> JobLogisticsManager.OnWorkerPunchIn → dispatching to BuildingLogisticsManager.OnWorkerPunchIn. worker='{_worker?.CharacterName}', workplace='{_workplace.BuildingName}'.");
            }
            _workplace.LogisticsManager.OnWorkerPunchIn(_worker);
        }
    }

    /// <summary>
    /// Vérifie si le worker est le propriétaire OU est dans ses heures de travail.
    /// Le propriétaire peut agir à tout moment.
    /// </summary>
    private bool IsOwnerOrOnSchedule()
    {
        if (_worker == null || _workplace == null) return false;

        // Le propriétaire individuel peut toujours agir
        if (_workplace.Owner == _worker) return true;

        // Sinon, vérifier si on est dans les heures de travail
        if (_worker.CharacterSchedule != null)
        {
            return _worker.CharacterSchedule.CurrentActivity == ScheduleActivity.Work;
        }

        return false;
    }

    public override void Execute()
    {
        if (_workplace == null || _workplace.LogisticsManager == null) return;

        // V?rifier les commandes qui n'ont pas pu être physiquement passées (ex: Cible occupée)
        _workplace.LogisticsManager.RetryUnplacedOrders(_worker);

        // Evaluer nos propres engagements envers les autres (BuyOrders reçues)
        _workplace.LogisticsManager.ProcessActiveBuyOrders();


        // Si on a une action en cours, l'exécuter
        if (_currentAction != null)
        {
            // Vérifier que l'action est encore valide
            if (!_currentAction.IsValid(_worker))
            {
                Debug.Log($"<color=orange>[JobLogistics]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification...");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                Debug.Log($"<color=cyan>[JobLogistics]</color> {_worker.CharacterName} : action {_currentAction.ActionName} terminée.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null; // Forcer la replanification
            }
            return;
        }

        // Pas d'action en cours → Planifier
        PlanNextActions();
    }

    private void PlanNextActions()
    {
        if (_workplace == null || _workplace.LogisticsManager == null) return;

        var worldState = new Dictionary<string, bool>
        {
            { "hasPendingOrders", _workplace.LogisticsManager.HasPendingOrders },
            { "isIdling", false }
        };

        _availableActions = new List<GoapAction>
        {
            new GoapAction_PlaceOrder(this),
            new GoapAction_StageItemForPickup(this),
            new GoapAction_GatherStorageItems(this),
            new GoapAction_IdleInCommercialBuilding(_workplace as CommercialBuilding)
        };

        GoapGoal targetGoal;
        if (_workplace.LogisticsManager.HasPendingOrders)
        {
            targetGoal = new GoapGoal("ProcessOrders", new Dictionary<string, bool> { { "hasPendingOrders", false } }, priority: 1);
        }
        else
        {
            targetGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);
        }

        _logisticsGoal = targetGoal;
        
        var validActions = _availableActions.Where(a => a.IsValid(_worker)).ToList();
        
        _currentPlan = GoapPlanner.Plan(worldState, validActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            Debug.Log($"<color=green>[JobLogistics]</color> {_worker.CharacterName} : nouveau plan ! Première action → {_currentAction.ActionName}");
        }
        else
        {
            Debug.Log($"<color=orange>[JobLogistics]</color> {_worker.CharacterName} : impossible de planifier.");
        }
    }
}
