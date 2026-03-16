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

    private float _nextActionTime = 0f;
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
            if (Time.time < _nextActionTime) return "Resting";
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

    private float _lastLogTime_NoOrder = 0f;
    private float _lastLogTime_NoStation = 0f;
    private float _lastLogTime_NoIngr = 0f;

    public override void Execute()
    {
        if (_worker == null || !(_workplace is CraftingBuilding cb)) return;

        if (Time.time < _nextActionTime)
        {
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
            if (Time.time > _lastLogTime_NoOrder + 5f)
            {
                Debug.Log($"<color=orange>[JobBlacksmith]</color> {_worker.CharacterName} ({cb.BuildingName}) : Aucune CraftingOrder disponible (ou manager vide).");
                _lastLogTime_NoOrder = Time.time;
            }
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
            if (Time.time > _lastLogTime_NoStation + 5f)
            {
                Debug.Log($"<color=orange>[JobBlacksmith]</color> {_worker.CharacterName} : Pas de station libre ou capable de faire {_currentOrder.ItemToCraft.ItemName}.");
                _lastLogTime_NoStation = Time.time;
            }
            return; // Attendre qu'une station se libère
        }

        // Vérifier les ingrédients
        if (_currentOrder.ItemToCraft.CraftingRecipe != null && _currentOrder.ItemToCraft.CraftingRecipe.Count > 0)
        {
            if (!cb.HasRequiredIngredients(_currentOrder.ItemToCraft.CraftingRecipe))
            {
                if (Time.time > _lastLogTime_NoIngr + 5f)
                {
                    Debug.Log($"<color=orange>[JobBlacksmith]</color> {_worker.CharacterName} : En attente de la livraison de matériaux par les transporteurs pour {_currentOrder.ItemToCraft.ItemName}.");
                    _lastLogTime_NoIngr = Time.time;
                }
                return;
            }
            else
            {
                Debug.Log($"<color=cyan>[JobBlacksmith]</color> {_worker.CharacterName} : Ingrédients validés pour {_currentOrder.ItemToCraft.ItemName}. En route vers la forge !");
            }
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
            // Consommer les ingrédients
            if (_workplace is CraftingBuilding cb && _currentOrder.ItemToCraft.CraftingRecipe != null)
            {
                foreach (var ingredient in _currentOrder.ItemToCraft.CraftingRecipe)
                {
                    for (int i = 0; i < ingredient.Amount; i++)
                    {
                        cb.TakeFromInventory(ingredient.Item);
                        // ItemInstance n'est pas un MonoBehaviour, sa suppression de l'inventaire suffit.
                    }
                }
            }

            _manager.UpdateCraftingOrderProgress(_currentOrder, 1);
        }

        if (RequiredSkill != null && _worker.CharacterSkills != null)
        {
            _worker.CharacterSkills.GainXP(RequiredSkill, 10);
        }

        _nextActionTime = Time.time + CRAFT_COOLDOWN;
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

    public override void OnWorkerPunchOut()
    {
        base.OnWorkerPunchOut();
        ResetCraftingState();
        _worker?.CharacterMovement?.ResetPath();
        
        // Nettoyer toute CharacterCraftAction qui serait encore en cours
        var currentAction = _worker?.CharacterActions?.CurrentAction;
        if (currentAction is CharacterCraftAction && _worker != null)
        {
            _worker.CharacterActions.ClearCurrentAction();
        }
    }

    public override void Unassign()
    {
        ResetCraftingState();
        _manager = null;
        _worker?.CharacterMovement?.ResetPath();
        base.Unassign();
    }
}
