using System.Collections.Generic;
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

        // Initialiser le GOAP si ce n'est pas fait
        if (_gatherGoal == null)
        {
            InitializeGOAP(gathering);
        }

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
    /// Initialise le système GOAP avec le goal et les actions disponibles.
    /// </summary>
    private void InitializeGOAP(GatheringBuilding building)
    {
        _gatherGoal = new GoapGoal(
            "DepositGatheredResources",
            new Dictionary<string, bool>
            {
                { "hasDepositedResources", true }
            },
            priority: 1
        );

        // Les actions sont recréées à chaque planification pour avoir des références fraîches
    }

    /// <summary>
    /// Construit le world state actuel et lance le planner.
    /// </summary>
    private void PlanNextActions(GatheringBuilding building)
    {
        // Vérifier si le building a encore besoin de ressources
        if (!building.NeedsResources())
        {
            Debug.Log($"<color=green>[JobGatherer]</color> {_worker.CharacterName} : le building n'a plus besoin de ressources.");
            return;
        }

        // Construire le world state
        bool hasAtLeastOneResource = false;

        // Check 1 : Le worker porte un item dans les mains (on suppose que c'est une ressource valide)
        var handsController = _worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.IsCarrying)
            hasAtLeastOneResource = true;

        // Check 2 : Le worker a des items acceptés par le building dans son sac
        var acceptedItems = building.GetAcceptedItems();
        var wantedItems = building.GetWantedItems();
        if (_worker.CharacterEquipment != null && _worker.CharacterEquipment.HaveInventory())
        {
            var inventory = _worker.CharacterEquipment.GetInventory();
            foreach (var slot in inventory.ItemSlots)
            {
                if (slot.IsEmpty()) continue;
                foreach (var item in acceptedItems)
                {
                    if (slot.ItemInstance.ItemSO == item)
                    {
                        hasAtLeastOneResource = true;
                        break;
                    }
                }
                if (hasAtLeastOneResource) break;
            }
        }

        // Check 2.1 : Et dans les mains aussi (seulement si c'est un item accepté)
        if (!hasAtLeastOneResource && handsController != null && handsController.IsCarrying)
        {
            foreach (var item in acceptedItems)
            {
                if (handsController.CarriedItem.ItemSO == item)
                {
                    hasAtLeastOneResource = true;
                    break;
                }
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
            else
            {
                // Vérifier si au moins UN wanted item peut encore rentrer dans le sac
                foreach (var wanted in wantedItems)
                {
                    if (equip.HasFreeSpaceForItemSO(wanted))
                    {
                        hasFreeSpace = true;
                        break;
                    }
                }
            }
        }

        // Logique GOAP intelligente :
        // Si on a des ressources mais qu'on a ENCORE de la place et qu'une zone existe, 
        // on ment au planner (hasResources=false) pour le forcer à continuer de Gather.
        bool hasResourcesForGoap = false;
        if (hasAtLeastOneResource)
        {
            if (!hasFreeSpace)
            {
                hasResourcesForGoap = true; // Plein à craquer -> aller déposer
            }
            else
            {
                if (building.HasGatherableZone)
                {
                    hasResourcesForGoap = false; // Continuer de gather
                }
                else
                {
                    hasResourcesForGoap = true; // Plus rien à gather -> aller déposer ce qu'on a
                }
            }
        }

        var worldState = new Dictionary<string, bool>
        {
            { "hasGatherZone", building.HasGatherableZone },
            { "hasResources", hasResourcesForGoap },
            { "hasDepositedResources", false }
        };

        // Créer les actions fraîches (chaque instance est stateful)
        _availableActions = new List<GoapAction>
        {
            new GoapAction_ExploreForResources(building),
            new GoapAction_GatherResources(building),
            new GoapAction_DepositResources(building)
        };

        // Planifier
        _currentPlan = GoapPlanner.Plan(worldState, _availableActions, _gatherGoal);

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
