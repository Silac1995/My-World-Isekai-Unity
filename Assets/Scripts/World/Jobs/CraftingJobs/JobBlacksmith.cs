using System.Linq;
using UnityEngine;

/// <summary>
/// Job de Forgeron : craft des armes et armures dans une ForgeBuilding.
/// Gère lui-même son état d'exécution de crafting (Recherche d'ordre -> Déplacement -> Crafting).
/// </summary>
public class JobBlacksmith : JobCrafter
{
    public override string JobTitle => "Forgeron";

    private CraftingStation _currentStation;
    private CraftingOrder _currentOrder;
    private JobLogisticsManager _manager;

    public CraftingStation CurrentStation => _currentStation;

    private float _cooldownTimer = 0f;
    private const float CRAFT_COOLDOWN = 1f;

    private enum CraftPhase
    {
        SearchingOrder,
        MovingToStation,
        ExecutingAction
    }

    private CraftPhase _currentPhase = CraftPhase.SearchingOrder;

    public override string CurrentActionName
    {
        get
        {
            if (_cooldownTimer > 0f) return "Resting";
            switch (_currentPhase)
            {
                case CraftPhase.SearchingOrder: return "Searching for orders";
                case CraftPhase.MovingToStation: return "Moving to Anvil";
                case CraftPhase.ExecutingAction: return $"Forging {_currentOrder?.ItemToCraft?.ItemName}";
                default: return "Idle";
            }
        }
    }

    public JobBlacksmith(SkillSO smithingSkill, SkillTier tier = SkillTier.Intermediate) : base(smithingSkill, tier)
    {
    }

    public override void Execute()
    {
        if (_worker == null || !(_workplace is CraftingBuilding cb)) return;

        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
            return;
        }

        var movement = _worker.CharacterMovement;
        if (movement == null) return;

        switch (_currentPhase)
        {
            case CraftPhase.SearchingOrder:
                HandleSearchOrder(cb);
                break;

            case CraftPhase.MovingToStation:
                HandleMovementToStation(movement);
                break;

            case CraftPhase.ExecutingAction:
                HandleCraftingExecution();
                break;
        }
    }

    private void HandleSearchOrder(CraftingBuilding cb)
    {
        if (_manager == null)
        {
            _manager = cb.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
        }

        if (_manager == null)
        {
            Debug.Log($"<color=orange>[JobBlacksmith]</color> {_worker.CharacterName} : Pas de Manager Logistique dans le bâtiment.");
            return;
        }

        _currentOrder = _manager.GetNextAvailableCraftingOrder();
        if (_currentOrder == null)
        {
            return; // En attente de commandes
        }

        // Trouver une station libre et compatible
        foreach (var room in cb.Rooms)
        {
            foreach (var station in room.GetFurnitureOfType<CraftingStation>())
            {
                if (station.CanCraft(_currentOrder.ItemToCraft) && (station.IsFree() || station.Occupant == _worker))
                {
                    _currentStation = station;
                    break;
                }
            }
            if (_currentStation != null) break;
        }

        if (_currentStation == null)
        {
            Debug.Log($"<color=orange>[JobBlacksmith]</color> {_worker.CharacterName} : Pas de station libre capable de crafter {_currentOrder.ItemToCraft.ItemName}.");
            return; // Attendre qu'une station se libère
        }

        _currentStation.Reserve(_worker);
        _currentPhase = CraftPhase.MovingToStation;
    }

    private void HandleMovementToStation(CharacterMovement movement)
    {
        if (_currentStation == null)
        {
            ResetCraftingState();
            return;
        }

        Vector3 targetPos = _currentStation.InteractionPoint != null ? _currentStation.InteractionPoint.position : _currentStation.transform.position;
        
        if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            if (Vector3.Distance(_worker.transform.position, targetPos) > movement.StoppingDistance + 0.5f)
            {
                movement.SetDestination(targetPos);
            }
            else
            {
                // Arrivé à la station
                movement.ResetPath();
                _currentStation.Use(_worker);

                Color targetColor = Color.white; 
                _worker.CharacterActions.ExecuteAction(new CharacterCraftAction(_worker, _currentOrder.ItemToCraft, targetColor, default));
                _currentPhase = CraftPhase.ExecutingAction;
            }
        }
    }

    private void HandleCraftingExecution()
    {
        var currentAction = _worker.CharacterActions.CurrentAction;
        
        if (currentAction != null && currentAction is CharacterCraftAction)
        {
            return; // On attend la fin
        }

        // L'action est terminée (succès ou annulée)
        // Vérifier si la station a pu crafter correctement
        if (_manager != null && _currentOrder != null)
        {
            _manager.UpdateCraftingOrderProgress(_currentOrder, 1);
        }

        if (RequiredSkill != null && _worker.CharacterSkills != null)
        {
            _worker.CharacterSkills.GainXP(RequiredSkill, 10);
        }

        _cooldownTimer = CRAFT_COOLDOWN;
        ResetCraftingState();
    }

    private void ResetCraftingState()
    {
        ReleaseStation();
        _currentOrder = null;
        _currentPhase = CraftPhase.SearchingOrder;
    }

    public void ReleaseStation()
    {
        if (_currentStation != null)
        {
            if (_currentStation.Occupant == _worker || _currentStation.ReservedBy == _worker)
            {
                _currentStation.Release();
            }
            _currentStation = null;
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is CraftingBuilding;
    }

    public override void Unassign()
    {
        ResetCraftingState();
        _manager = null;
        _worker?.CharacterMovement?.ResetPath();
        base.Unassign();
    }
}
