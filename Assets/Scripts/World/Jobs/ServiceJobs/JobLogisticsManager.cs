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

    // PlanNextActions hot path — called every tick while the boss has no current action.
    // Pre-allocate once; reuse forever. Without this, each tick allocates a new worldState dict,
    // a new List of 4 new GoapAction instances, a new GoapGoal with a new dict, and the LINQ
    // `.Where().ToList()` enumerator+list pair. With N bosses × 10Hz that's thousands of
    // short-lived allocations/sec → GC pressure that manifests as progressive host-side freeze.
    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(4);
    private readonly List<GoapAction> _scratchValidActions = new List<GoapAction>(8);
    private GoapGoal _cachedProcessOrdersGoal;
    private GoapGoal _cachedIdleGoal;

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
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=orange>[JobLogistics]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification...");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                if (NPCDebug.VerboseJobs)
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

        // Reuse the scratch world-state dict (allocation-free after warm-up).
        _scratchWorldState.Clear();
        bool hasOrders = _workplace.LogisticsManager.HasPendingOrders;
        _scratchWorldState["hasPendingOrders"] = hasOrders;
        _scratchWorldState["isIdling"] = false;

        // GoapAction instances are stateful per-plan (`_isComplete`, `_isMoving`, etc.). Caching
        // the SAME instance across multiple plans risks stale state leaking when the BT branches
        // out of Work mid-action without propagating Exit() down to the Job's _currentAction —
        // PlaceOrder.IsValid then returns false because `_isComplete=true` was carried over, the
        // planner can't satisfy hasPendingOrders=false, and any subsequent BuyOrders in the queue
        // (e.g. tshirt + jeans for a clothing shop where wood was already placed) stay stuck.
        //
        // Mirror the JobHarvester / JobTransporter pattern: reuse the List wrapper to save the
        // single allocation, but Clear() and re-add fresh action instances each plan so state
        // can never leak. The 4 ctors per plan are negligible vs. the GoapPlanner work.
        if (_availableActions == null) _availableActions = new List<GoapAction>(4);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_PlaceOrder(this));
        _availableActions.Add(new GoapAction_StageItemForPickup(this));
        _availableActions.Add(new GoapAction_GatherStorageItems(this));
        _availableActions.Add(new GoapAction_IdleInCommercialBuilding(_workplace as CommercialBuilding));

        // Cache the two possible goals (stable targets with stable DesiredState dicts).
        if (_cachedProcessOrdersGoal == null)
            _cachedProcessOrdersGoal = new GoapGoal("ProcessOrders", new Dictionary<string, bool> { { "hasPendingOrders", false } }, priority: 1);
        if (_cachedIdleGoal == null)
            _cachedIdleGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);

        GoapGoal targetGoal = hasOrders ? _cachedProcessOrdersGoal : _cachedIdleGoal;
        _logisticsGoal = targetGoal;

        // Reuse the valid-actions scratch list (allocation-free after warm-up). Replaces the
        // `_availableActions.Where(a => a.IsValid(_worker)).ToList()` enumerator+list allocation.
        _scratchValidActions.Clear();
        for (int i = 0; i < _availableActions.Count; i++)
        {
            var a = _availableActions[i];
            if (a.IsValid(_worker)) _scratchValidActions.Add(a);
        }

        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[JobLogistics]</color> {_worker.CharacterName} : nouveau plan ! Première action → {_currentAction.ActionName}");
        }
        else if (NPCDebug.VerboseJobs)
        {
            // Without the gate this fires every Execute tick (10Hz) whenever a worker has no viable plan —
            // the main driver of progressive Unity-console filling under heavy worker load.
            Debug.Log($"<color=orange>[JobLogistics]</color> {_worker.CharacterName} : impossible de planifier.");
        }
    }
}
