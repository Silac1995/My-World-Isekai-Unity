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

    // Furniture-source pickup path. When non-null, the transporter is targeting an item
    // sitting in a StorageFurniture slot rather than a loose WorldItem. Mutually exclusive
    // with TargetWorldItem — GoapAction_LocateItem clears the loose target whenever it
    // commits to a slot pickup, and the planner is gated so PickupItem/MoveToItem refuse
    // to run while these are set (see their IsValid early-out). Cleared anywhere
    // TargetWorldItem is cleared.
    public StorageFurniture TargetSourceFurniture { get; set; }
    public ItemInstance TargetItemFromFurniture { get; set; }

    public bool ForceDeliverPartialBatch { get; set; } = false;
    public float WaitCooldown { get; set; } = 0f;

    // --- GOAP ---
    private GoapGoal _transporterGoal;
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    // Per-tick allocation pool (see PlanNextActions). Worldstate + goals are stable across ticks,
    // the action list is reused (instances are still recreated — they're stateful per-plan).
    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(7);
    private List<GoapAction> _availableActions;
    private GoapGoal _cachedDeliverItemsGoal;
    private GoapGoal _cachedIdleGoal;

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
            TargetSourceFurniture = null;
            TargetItemFromFurniture = null;

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
            if (CurrentOrder != null) CurrentOrder.RemoveInTransit(1);
            
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
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=orange>[JobTransporter]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification... (TargetWorldItem = {(TargetWorldItem != null ? TargetWorldItem.name : "NULL")})");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            // _currentAction may have been set to null by CancelCurrentOrder() internally inside Execute!
            if (_currentAction != null && _currentAction.IsComplete)
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=cyan>[JobTransporter]</color> {_worker.CharacterName} : action {_currentAction.ActionName} terminée.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                // Si on a d'autres actions dans le plan, on prend la suivante,
                // sinon on met le plan à null pour forcer un nouveau cycle.
                if (_currentPlan != null && _currentPlan.Count > 0)
                {
                    _currentAction = _currentPlan.Dequeue();
                    if (NPCDebug.VerboseJobs)
                        Debug.Log($"<color=green>[JobTransporter]</color> {_worker.CharacterName} : passe à l'action suivante → {_currentAction.ActionName} (TargetWorldItem = {(TargetWorldItem != null ? TargetWorldItem.name : "NULL")})");
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
        if (NPCDebug.VerboseJobs && !itemLocated && _currentPlan != null) Debug.Log($"[JobTransporter] PlanNextActions triggered while TargetWorldItem is NULL.");
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
                // InTransitQuantity already includes this worker's CarriedItems.Count since it's incremented during AddCarriedItem.
                // If the global unfulfilled need is 0 or less, we have enough and should proceed to delivery.
                int globallyStillNeeded = CurrentOrder.Quantity - CurrentOrder.DeliveredQuantity - CurrentOrder.InTransitQuantity;
                bool hasEnough = globallyStillNeeded <= 0;
                
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
                    TargetSourceFurniture = null;
                    TargetItemFromFurniture = null;
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

        // Reuse the scratch world-state dict (allocation-free after warm-up).
        _scratchWorldState.Clear();
        _scratchWorldState["atSourceStorage"] = atSourceStorage;
        _scratchWorldState["itemLocated"] = itemLocated;
        _scratchWorldState["atItem"] = atItem;
        _scratchWorldState["itemCarried"] = itemCarried;
        _scratchWorldState["atDestination"] = atDestination;
        _scratchWorldState["itemDelivered"] = itemDelivered;
        _scratchWorldState["isIdling"] = false;

        // Action instances are stateful (per-plan state like _currentAction phase) so we recreate
        // them each plan — but we reuse the List wrapper to avoid one allocation per tick.
        if (_availableActions == null) _availableActions = new List<GoapAction>(8);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_GoToSourceStorage(this));
        _availableActions.Add(new GoapAction_LocateItem(this));
        _availableActions.Add(new GoapAction_MoveToItem(this));
        _availableActions.Add(new GoapAction_PickupItem(this));
        // Furniture-source pickup. Only registered when LocateItem has already committed
        // to the furniture path (TargetSourceFurniture set). The planner does not call
        // IsValid during search, so registering this unconditionally would let it pick
        // the cheapest TakeFromSourceFurniture plan even when the loose-WorldItem path
        // is active — runtime IsValid would then fail every tick and the resulting
        // replan would re-pick the same invalid action (busy loop). Gating on
        // TargetSourceFurniture keeps the planner's cost-driven preference intact while
        // making the path mutually exclusive with MoveToItem/PickupItem.
        if (TargetSourceFurniture != null)
            _availableActions.Add(new GoapAction_TakeFromSourceFurniture(this));
        _availableActions.Add(new GoapAction_MoveToDestination(this));
        _availableActions.Add(new GoapAction_DeliverItem(this));
        _availableActions.Add(new GoapAction_IdleInCommercialBuilding(_workplace as CommercialBuilding));

        // Cache the two goals (stable DesiredState dicts).
        if (_cachedDeliverItemsGoal == null)
            _cachedDeliverItemsGoal = new GoapGoal("DeliverItems", new Dictionary<string, bool> { { "itemDelivered", true } }, priority: 1);
        if (_cachedIdleGoal == null)
            _cachedIdleGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);

        GoapGoal targetGoal = CurrentOrder != null ? _cachedDeliverItemsGoal : _cachedIdleGoal;
        _transporterGoal = targetGoal;

        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _availableActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[JobTransporter]</color> {_worker.CharacterName} : nouveau plan GOAP ! Première action → {_currentAction.ActionName}");
        }
        else if (NPCDebug.VerboseJobs)
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
        
        if (CurrentOrder != null && CarriedItems.Count > 0)
        {
            CurrentOrder.RemoveInTransit(CarriedItems.Count);
        }
        
        CarriedItems.Clear();
        TargetWorldItem = null;
        TargetSourceFurniture = null;
        TargetItemFromFurniture = null;
        CurrentOrder = null;

        base.Unassign();
    }

    public override bool HasWorkToDo()
    {
        if (CurrentOrder != null && !CurrentOrder.IsCompleted)
            return true;

        if (_workplace != null)
        {
            var manager = _workplace.LogisticsManager;
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
            // Update the Transporter's internal LogisticsManager
            var manager = _workplace.LogisticsManager;
            if (manager != null)
            {
                manager.UpdateTransportOrderProgress(CurrentOrder, amount);
            }

            // Wage system hook: credit the worker's WorkLog for each item delivered.
            // Credit goes to the worker's EMPLOYER (_workplace), not the delivery destination.
            TryCreditWorkLog(amount);

            if (CurrentOrder.IsCompleted)
            {
                if (CarriedItems.Count > 0)
                {
                    CurrentOrder.RemoveInTransit(CarriedItems.Count);
                }
                
                CurrentOrder = null;
                TargetWorldItem = null;
                TargetSourceFurniture = null;
                TargetItemFromFurniture = null;
                if (_currentAction != null)
                {
                    _currentAction.Exit(_worker);
                    _currentAction = null;
                }
                _currentPlan = null;
                CarriedItems.Clear();
            }
        }
    }

    public void CancelCurrentOrder(bool dropFromQueue = false)
    {
        Debug.Log($"<color=orange>[JobTransporter]</color> {_worker?.CharacterName} annule sa livraison en cours.");
        
        if (CurrentOrder != null && CarriedItems.Count > 0)
        {
            CurrentOrder.RemoveInTransit(CarriedItems.Count);
        }
        
        if (dropFromQueue && CurrentOrder != null)
        {
            var myManager = _workplace.LogisticsManager;
            if (myManager != null)
            {
                myManager.CancelActiveTransportOrder(CurrentOrder);
            }
        }

        CurrentOrder = null;
        CarriedItems.Clear();
        TargetWorldItem = null;
        TargetSourceFurniture = null;
        TargetItemFromFurniture = null;

        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;
    }

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "En attente de commandes";
    public override string CurrentGoalName => _transporterGoal != null ? _transporterGoal.GoalName : "No Goal";

    private void TryCreditWorkLog(int amount)
    {
        if (amount <= 0 || _workplace == null) return;
        var worker = Worker;
        if (worker == null) return;
        var workLog = worker.CharacterWorkLog;
        if (workLog == null) return;

        // Use this transporter's JobType (always Transporter) and the employer building's
        // stable BuildingId (GUID, not GameObject name) — see Building.BuildingId. Keeps
        // history rename-proof, matches CommercialBuilding.GetBuildingIdForWorklog.
        string buildingId = _workplace.BuildingId;
        workLog.LogShiftUnit(MWI.WorldSystem.JobType.Transporter, buildingId, amount);
    }
}
