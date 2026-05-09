using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Job de type Harvester : récolte des ressources selon ce que le HarvestingBuilding dicte.
/// Utilise le système GOAP pour planifier ses actions :
/// 1. Explorer pour trouver une zone de Harvestable (si pas encore trouvée)
/// 2. Se rendre à la zone et récolter
/// 3. Déposer les ressources à la zone de dépôt
/// Puis recommencer le cycle.
/// </summary>
[System.Serializable]
public class JobHarvester : Job
{
    [SerializeField] private string _jobTitle;
    [SerializeField] private JobType _jobType;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Harvester;
    public override JobType Type => _jobType;

    // Heavy-planning job: GOAP plan + per-tick task scans against the building's
    // BuildingTaskManager. Reactivity comes from the GOAP plan itself (which still
    // ticks per-frame inside CharacterMovement / CharacterActions); 3.3 Hz here is
    // plenty for the planning layer. See wiki/projects/optimisation-backlog.md
    // entry #2 / Cₐ.
    public override float ExecuteIntervalSeconds => 0.3f;

    // GOAP
    private GoapGoal _harvestGoal;
    private List<GoapAction> _availableActions;
    // Pre-filtered subset of _availableActions where IsValid(worker) is true at planning time.
    // GoapPlanner.Plan does NOT call IsValid — it only matches Preconditions/Effects against the
    // world-state dict and picks the lowest-cost chain. Without this filter, two actions with
    // identical Preconditions+Effects but different Cost (e.g. HarvestResources@1 vs
    // DestroyHarvestable@5) make the planner always pick the cheaper one, even when its IsValid
    // returns false (no claimable HarvestResourceTask). The action then completes immediately in
    // Execute → planner replans → picks it again → infinite Idle/HarvestResources loop. Mirrors
    // JobLogisticsManager._scratchValidActions.
    private List<GoapAction> _scratchValidActions = new List<GoapAction>(6);
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    // Per-tick allocation pool (see PlanNextActions). Without these, each tick allocated a
    // new worldState dict, a new List of 5 new GoapAction objects, and two new GoapGoal instances
    // with their own dicts — a dominant GC source under heavy worker load.
    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(6);
    private GoapGoal _cachedIdleGoal;
    private GoapGoal _cachedHarvestAndDepositGoal;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _harvestGoal != null ? _harvestGoal.GoalName : "No Goal";

    public JobHarvester(string jobTitle = "Harvester", JobType jobType = JobType.None)
    {
        _jobTitle = jobTitle;
        _jobType = jobType;
    }

    /// <summary>
    /// Exécuté chaque tick quand le worker est au travail.
    /// Utilise le GOAP planner pour décider quoi faire.
    /// </summary>
    public override void Execute()
    {
        if (_workplace == null || !(_workplace is HarvestingBuilding harvesting)) return;

        // Si on a une action en cours, l'exécuter
        if (_currentAction != null)
        {
            // Vérifier que l'action est encore valide
            if (!_currentAction.IsValid(_worker))
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=orange>[JobHarvester]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification...");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                if (NPCDebug.VerboseJobs)
                    Debug.Log($"<color=cyan>[JobHarvester]</color> {_worker.CharacterName} : action {_currentAction.ActionName} terminée.");
                _currentAction.Exit(_worker);
                _currentAction = null;

                // Forcer la replanification à chaque fois pour évaluer la capacité restante.
                // Au lieu de dépiler un plan devenu obsolète (ex: Deposit alors qu'on a encore de la place).
                _currentPlan = null;
            }
            return;
        }

        // Pas d'action en cours → Planifier
        PlanNextActions(harvesting);
    }

    /// <summary>
    /// Construit le world state actuel et lance le planner avec un nouvel objectif calculé dynamiquement.
    /// </summary>
    private void PlanNextActions(HarvestingBuilding building)
    {
        // Construire le world state
        bool hasAtLeastOneResource = false;
        var handsController = _worker.CharacterVisual?.BodyPartsController?.HandsController;
        
        var acceptedItems = building.GetAcceptedItems();
        var wantedItems = building.GetWantedItems();

        // Check 1 : Le worker a des items acceptés par le building dans son sac ?
        if (_worker.CharacterEquipment != null && _worker.CharacterEquipment.HaveInventory())
        {
            hasAtLeastOneResource = _worker.CharacterEquipment.GetInventory().HasAnyItemSO(acceptedItems);
        }

        // Check 2 : Et dans les mains ?
        if (!hasAtLeastOneResource && handsController != null && handsController.IsCarrying)
        {
            if (acceptedItems.Contains(handsController.CarriedItem.ItemSO))
            {
                hasAtLeastOneResource = true;
            }
        }

        // Check 3 : Le worker a-t-il encore de la place ?
        bool hasFreeSpace = false;
        var equip = _worker.CharacterEquipment;
        if (equip != null)
        {
            if (handsController != null && handsController.AreHandsFree())
            {
                hasFreeSpace = true;
            }
            else if (equip.HaveInventory())
            {
                // Vérifier si au moins UN wanted item peut encore rentrer dans le sac
                hasFreeSpace = equip.GetInventory().HasFreeSpaceForAnyItemSO(wantedItems);
            }
        }

        // Logique GOAP intelligente :
        // Si on a des ressources mais qu'on a ENCORE de la place et qu'une zone existe, 
        // on ment au planner (hasResources=false) pour le forcer à continuer de Gather.
        bool hasResourcesForGoap = false;
        bool allResourcesHarvested = building.AreAllRequestedResourcesHarvested();
        bool needsToWork = !allResourcesHarvested;

        if (hasAtLeastOneResource)
        {
            if (!hasFreeSpace)
            {
                hasResourcesForGoap = true; // Plein à craquer -> aller déposer
            }
            else
            {
                if (building.HasHarvestableZone && needsToWork)
                {
                    hasResourcesForGoap = false; // Continuer de gather
                }
                else
                {
                    hasResourcesForGoap = true; // Plus rien à gather ou fini le quota -> aller déposer ce qu'on a
                }
            }
        }

        // Planification intelligente du Pickup vs Gather
        bool looseItemExists = false;
        bool canHarvest = false;
        bool hasValidHarvestTasks = false;
        
        if (building.TaskManager != null)
        {
            looseItemExists = building.TaskManager.HasAvailableOrClaimedTask<PickupLooseItemTask>(_worker, task => 
            {
                var interactable = task.Target as WorldItem;
                return interactable != null && !_worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID());
            });

            canHarvest = building.TaskManager.HasAvailableOrClaimedTask<HarvestResourceTask>(_worker, task =>
            {
                var interactable = task.Target as Harvestable;
                if (interactable == null || _worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID())) return false;
                return interactable.HasAnyYieldOutput(building.GetWantedItems());
            });

            // Also count destroy tasks — without this, a building whose only sources of
            // wanted items are destruction-only nodes (e.g. apple trees that drop wood on
            // chop) would have hasValidHarvestTasks = false, the planner would pick Idle,
            // and workers would never start the destroy chain. The DestroyHarvestableTask's
            // own IsValid check still gates on AllowNpcDestruction so designer opt-out works.
            bool canDestroy = building.TaskManager.HasAvailableOrClaimedTask<DestroyHarvestableTask>(_worker, task =>
            {
                var interactable = task.Target as Harvestable;
                if (interactable == null || _worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID())) return false;
                if (!interactable.AllowDestruction || !interactable.AllowNpcDestruction) return false;
                return interactable.HasAnyDestructionOutput(building.GetWantedItems());
            });
            canHarvest = canHarvest || canDestroy;

            bool hasValidYieldTasks = building.TaskManager.HasAnyTaskOfType<HarvestResourceTask>(task =>
            {
                var interactable = task.Target as Harvestable;
                return interactable != null && interactable.HasAnyYieldOutput(building.GetWantedItems());
            });
            bool hasValidDestroyTasks = building.TaskManager.HasAnyTaskOfType<DestroyHarvestableTask>(task =>
            {
                var interactable = task.Target as Harvestable;
                return interactable != null
                    && interactable.AllowDestruction
                    && interactable.AllowNpcDestruction
                    && interactable.HasAnyDestructionOutput(building.GetWantedItems());
            });
            hasValidHarvestTasks = hasValidYieldTasks || hasValidDestroyTasks;
        }

        // Reuse the scratch world-state dict (cleared + repopulated each tick — zero allocation after warm-up).
        _scratchWorldState.Clear();
        _scratchWorldState["hasHarvestZone"] = hasValidHarvestTasks; // True only if tasks exist, forces ExploreForResources
        _scratchWorldState["looseItemExists"] = looseItemExists;
        _scratchWorldState["hasResources"] = hasResourcesForGoap;
        _scratchWorldState["hasDepositedResources"] = false;
        _scratchWorldState["needsToWork"] = needsToWork;
        _scratchWorldState["isIdling"] = false;

        // Action instances are stateful (per-plan _currentTarget/_isComplete), so we must
        // create fresh ones each plan. But the LIST wrapper and the GoapGoals are stable —
        // pool those to avoid per-tick allocations.
        if (_availableActions == null) _availableActions = new List<GoapAction>(5);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_ExploreForHarvestables(building));
        _availableActions.Add(new GoapAction_HarvestResources(building));
        // Destruction-path fallback. Higher Cost (5f vs 1f for harvest) so the planner only
        // picks this when there's no harvest alternative — gated server-side on
        // Harvestable.AllowNpcDestruction (designer opt-in per node).
        _availableActions.Add(new GoapAction_DestroyHarvestable(building));
        _availableActions.Add(new GoapAction_PickupLooseItem(building));
        _availableActions.Add(new GoapAction_DepositResources(building));
        _availableActions.Add(new GoapAction_IdleInBuilding(building));

        // Cache both goals (their DesiredState dicts are constant).
        if (_cachedIdleGoal == null)
            _cachedIdleGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);
        if (_cachedHarvestAndDepositGoal == null)
            _cachedHarvestAndDepositGoal = new GoapGoal("HarvestAndDeposit", new Dictionary<string, bool> { { "hasDepositedResources", true } }, priority: 1);

        bool trulyFinishedWork = allResourcesHarvested && !hasAtLeastOneResource;
        bool stuckWaitingForTrees = building.HasHarvestableZone && !canHarvest && !looseItemExists && !hasAtLeastOneResource;

        GoapGoal targetGoal = (trulyFinishedWork || stuckWaitingForTrees)
            ? _cachedIdleGoal
            : _cachedHarvestAndDepositGoal;

        _harvestGoal = targetGoal; // On sauvegarde l'objectif courant pour l'UI de Debug

        // Pre-filter actions by IsValid(_worker) — see _scratchValidActions field comment.
        _scratchValidActions.Clear();
        for (int i = 0; i < _availableActions.Count; i++)
        {
            var a = _availableActions[i];
            if (a.IsValid(_worker)) _scratchValidActions.Add(a);
        }

        // Planifier
        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[JobHarvester]</color> {_worker.CharacterName} : nouveau plan ! Première action → {_currentAction.ActionName}");
        }
        else if (NPCDebug.VerboseJobs)
        {
            Debug.Log($"<color=orange>[JobHarvester]</color> {_worker.CharacterName} : impossible de planifier.");
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is HarvestingBuilding;
    }

    /// <summary>
    /// Spécifie si le harvester a encore de la récolte ou du dépôt à faire.
    /// Renvoie Faux s'il n'a plus rien sur lui et que le batiment n'a plus besoin de rien.
    /// </summary>
    public override bool HasWorkToDo()
    {
        if (_workplace is not HarvestingBuilding building) return false;

        bool hasAtLeastOneResource = _worker.CharacterEquipment != null && 
                                     _worker.CharacterEquipment.HaveInventory() && 
                                     _worker.CharacterEquipment.GetInventory().ItemSlots.Any(slot => !slot.IsEmpty());
                                     
        if (_worker.CharacterVisual?.BodyPartsController?.HandsController != null)
        {
            if (_worker.CharacterVisual.BodyPartsController.HandsController.IsCarrying)
            {
                hasAtLeastOneResource = true;
            }
        }

        bool allResourcesHarvested = building.AreAllRequestedResourcesHarvested();

        // Le travail est fini si : 
        // 1. On n'a plus rien sur nous pour déposer
        // 2. Le batiment a toutes les ressources voulues OU il n'y a plus de zone avec ressources
        if (allResourcesHarvested && !hasAtLeastOneResource)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Les harvesters commencent tôt le matin.
    /// </summary>
    public override List<ScheduleEntry> GetWorkSchedule()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry(6, 16, ScheduleActivity.Work, 10)
        };
    }

    /// <summary>
    /// Override Assign pour ajouter l'employé au building.
    /// </summary>
    public override void Assign(Character worker, CommercialBuilding workplace)
    {
        base.Assign(worker, workplace);

        if (workplace is HarvestingBuilding harvesting)
        {
            harvesting.AddEmployee(worker);
        }
    }

    /// <summary>
    /// Override Unassign pour retirer l'employé du building.
    /// </summary>
    public override void Unassign()
    {
        if (_workplace is HarvestingBuilding harvesting && _worker != null)
        {
            harvesting.RemoveEmployee(_worker);
        }

        // Cleanup GOAP
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;

        base.Unassign();
    }
}
