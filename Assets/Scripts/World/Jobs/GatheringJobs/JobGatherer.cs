using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Job de type Gatherer : récolte des ressources selon ce que le GatheringBuilding dicte.
/// Utilise le système GOAP pour planifier ses actions :
/// 1. Explorer pour trouver une zone de GatherableObject (si pas encore trouvée)
/// 2. Se rendre à la zone et récolter
/// 3. Déposer les ressources à la zone de dépôt
/// Puis recommencer le cycle.
/// </summary>
public class JobGatherer : Job
{
    private string _jobTitle;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Gatherer;

    // GOAP
    private GoapGoal _gatherGoal;
    private List<GoapAction> _availableActions;
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _gatherGoal != null ? _gatherGoal.GoalName : "No Goal";

    public JobGatherer(string jobTitle = "Gatherer")
    {
        _jobTitle = jobTitle;
    }

    /// <summary>
    /// Exécuté chaque tick quand le worker est au travail.
    /// Utilise le GOAP planner pour décider quoi faire.
    /// </summary>
    public override void Execute()
    {
        if (_workplace == null || !(_workplace is GatheringBuilding gathering)) return;

        // Si on a une action en cours, l'exécuter
        if (_currentAction != null)
        {
            // Vérifier que l'action est encore valide
            if (!_currentAction.IsValid(_worker))
            {
                Debug.Log($"<color=orange>[JobGatherer]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification...");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                Debug.Log($"<color=cyan>[JobGatherer]</color> {_worker.CharacterName} : action {_currentAction.ActionName} terminée.");
                _currentAction.Exit(_worker);
                _currentAction = null;

                // Forcer la replanification à chaque fois pour évaluer la capacité restante.
                // Au lieu de dépiler un plan devenu obsolète (ex: Deposit alors qu'on a encore de la place).
                _currentPlan = null;
            }
            return;
        }

        // Pas d'action en cours → Planifier
        PlanNextActions(gathering);
    }

    /// <summary>
    /// Construit le world state actuel et lance le planner avec un nouvel objectif calculé dynamiquement.
    /// </summary>
    private void PlanNextActions(GatheringBuilding building)
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
        bool allResourcesGathered = building.AreAllRequestedResourcesGathered();
        bool needsToWork = !allResourcesGathered;

        if (hasAtLeastOneResource)
        {
            if (!hasFreeSpace)
            {
                hasResourcesForGoap = true; // Plein à craquer -> aller déposer
            }
            else
            {
                if (building.HasGatherableZone && needsToWork)
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
        bool canGather = false;
        
        if (building.TaskManager != null)
        {
            looseItemExists = building.TaskManager.HasAvailableOrClaimedTask<PickupLooseItemTask>(_worker, task => 
            {
                var interactable = task.Target as WorldItem;
                return interactable != null && !_worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID());
            });

            canGather = building.TaskManager.HasAvailableOrClaimedTask<GatherResourceTask>(_worker, task => 
            {
                var interactable = task.Target as GatherableObject;
                return interactable != null && !_worker.PathingMemory.IsBlacklisted(interactable.gameObject.GetInstanceID());
            });

            // If we have a zone memory, but absolutely ZERO tasks exist for it (not even claimed ones), the zone is truly dead.
            if (building.HasGatherableZone)
            {
                bool anyGatherTaskExists = building.TaskManager.HasAnyTaskOfType<GatherResourceTask>();
                if (!anyGatherTaskExists)
                {
                    Debug.Log($"<color=orange>[JobGatherer]</color> {_worker.CharacterName}: The active gathering zone has 0 physical trees remaining. Clearing zone memory.");
                    building.ClearGatherableZone();
                }
            }
        }

        var worldState = new Dictionary<string, bool>
        {
            { "hasGatherZone", building.HasGatherableZone },
            { "looseItemExists", looseItemExists },
            { "hasResources", hasResourcesForGoap },
            { "hasDepositedResources", false },
            { "needsToWork", needsToWork },
            { "isIdling", false }
        };

        // Créer les actions fraîches (chaque instance est stateful)
        _availableActions = new List<GoapAction>
        {
            new GoapAction_ExploreForResources(building),
            new GoapAction_GatherResources(building),
            new GoapAction_PickupLooseItem(building),
            new GoapAction_DepositResources(building),
            new GoapAction_IdleInBuilding(building)
        };

        // Définir l'objectif prioritaire
        GoapGoal targetGoal;
        
        bool trulyFinishedWork = allResourcesGathered && !hasAtLeastOneResource;
        bool stuckWaitingForTrees = building.HasGatherableZone && !canGather && !looseItemExists && !hasAtLeastOneResource;

        if (trulyFinishedWork || stuckWaitingForTrees)
        {
            targetGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);
        }
        else
        {
            targetGoal = new GoapGoal("GatherAndDeposit", new Dictionary<string, bool> { { "hasDepositedResources", true } }, priority: 1);
        }

        _gatherGoal = targetGoal; // On sauvegarde l'objectif courant pour l'UI de Debug
        
        // Planifier
        _currentPlan = GoapPlanner.Plan(worldState, _availableActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            Debug.Log($"<color=green>[JobGatherer]</color> {_worker.CharacterName} : nouveau plan ! Première action → {_currentAction.ActionName}");
        }
        else
        {
            Debug.Log($"<color=orange>[JobGatherer]</color> {_worker.CharacterName} : impossible de planifier.");
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is GatheringBuilding;
    }

    /// <summary>
    /// Spécifie si le gatherer a encore de la récolte ou du dépôt à faire.
    /// Renvoie Faux s'il n'a plus rien sur lui et que le batiment n'a plus besoin de rien.
    /// </summary>
    public override bool HasWorkToDo()
    {
        if (_workplace is not GatheringBuilding building) return false;

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

        bool allResourcesGathered = building.AreAllRequestedResourcesGathered();

        // Le travail est fini si : 
        // 1. On n'a plus rien sur nous pour déposer
        // 2. Le batiment a toutes les ressources voulues OU il n'y a plus de zone avec ressources
        if (allResourcesGathered && !hasAtLeastOneResource)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Les gatherers commencent tôt le matin.
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

        if (workplace is GatheringBuilding gathering)
        {
            gathering.AddEmployee(worker);
        }
    }

    /// <summary>
    /// Override Unassign pour retirer l'employé du building.
    /// </summary>
    public override void Unassign()
    {
        if (_workplace is GatheringBuilding gathering && _worker != null)
        {
            gathering.RemoveEmployee(_worker);
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
