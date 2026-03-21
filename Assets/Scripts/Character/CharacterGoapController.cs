using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Contrôleur central pour le GOAP lié à l'individu (vie, besoins, objectifs personnels).
/// Contrairement au GOAP des Jobs (ex: JobHarvester), celui-ci gère les actions
/// permanentes et les buts de vie du personnage.
/// </summary>
public class CharacterGoapController : CharacterSystem
{
    [Header("Settings")]
    [SerializeField] private float _planReevaluationInterval = 2f;
    
    // État actuel
    private GoapGoal _currentGoal;
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;
    private Dictionary<string, bool> _worldState = new Dictionary<string, bool>();
    
    private float _timer;

    public Character Character => _character;
    public GoapAction CurrentAction => _currentAction;
    public string CurrentGoalName => _currentGoal?.GoalName ?? "None";

    protected override void Awake()
    {
        base.Awake();
    }

    protected override void HandleIncapacitated(Character character)
    {
        CancelPlan();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (inCombat) CancelPlan();
    }

    /// <summary>
    /// Met à jour le monde intérieur du NPC pour le planner.
    /// </summary>
    public void UpdateWorldState()
    {
        _worldState.Clear();
        
        if (_character.CharacterNeeds != null)
        {
            foreach (var need in _character.CharacterNeeds.AllNeeds)
            {
                var goal = need.GetGoapGoal();
                if (goal != null && goal.DesiredState != null)
                {
                    foreach (var kvp in goal.DesiredState)
                    {
                        // If need is active, the desired state is NOT met (we invert the desired boolean).
                        // If need is inactive, the desired state IS met (we match the desired boolean).
                        _worldState[kvp.Key] = need.IsActive() ? !kvp.Value : kvp.Value;
                    }
                }
            }
        }

        // 3. Connaissance locale (Sensors)
        // Note: Dans une version avancée, on checkerait ici si le personnage connaît un boss/building
        _worldState["knowsVacantJob"] = CheckForJobKnowledge();
        _worldState["atBossLocation"] = CheckAtBossLocation();
    }

    private bool CheckForJobKnowledge()
    {
        if (BuildingManager.Instance == null) return false;
        
        var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>(true);
        if (building != null)
        {
            return true;
        }
        
        Debug.LogWarning($"<color=orange>[GOAP Sensor]</color> {_character.CharacterName} doesn't know any vacant jobs with a boss.");
        return false;
    }

    private bool CheckAtBossLocation()
    {
        if (_character.CharacterJob == null || _character.CharacterJob.HasJob) return false;
        
        var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>(true);
        if (building == null || !building.HasOwner) return false;

        float dist = Vector3.Distance(_character.transform.position, building.Owner.transform.position);
        return dist < 2.5f; // Distance d'interaction
    }

    /// <summary>
    /// Tente de trouver un plan pour satisfaire le but le plus urgent.
    /// </summary>
    public bool Replan()
    {
        UpdateWorldState();
        
        List<GoapGoal> potentialGoals = new List<GoapGoal>();

        // Dynamic Goal Injection from CharacterNeeds
        if (_character.CharacterNeeds != null)
        {
            foreach (var need in _character.CharacterNeeds.AllNeeds)
            {
                if (need.IsActive())
                {
                    potentialGoals.Add(need.GetGoapGoal());
                }
            }
        }

        // Trier par priorité et tenter de planifier
        potentialGoals = potentialGoals.OrderByDescending(g => g.Priority).ToList();

        // Récupérer les actions disponibles pour la "Vie"
        var availableActions = GetLifeActions();

        foreach (var goal in potentialGoals)
        {
            var plan = GoapPlanner.Plan(_worldState, availableActions, goal);
            if (plan != null && plan.Count > 0)
            {
                _currentGoal = goal;
                _currentPlan = plan;
                _currentAction = _currentPlan.Dequeue();
                return true;
            }
        }

        return false;
    }

    private List<GoapAction> GetLifeActions()
    {
        List<GoapAction> actions = new List<GoapAction>();

        if (_character.CharacterNeeds != null)
        {
            foreach (var need in _character.CharacterNeeds.AllNeeds)
            {
                if (need.IsActive())
                {
                    foreach (var action in need.GetGoapActions())
                    {
                        if (action.IsValid(_character))
                        {
                            actions.Add(action);
                        }
                    }
                }
            }
        }

        return actions;
    }

    /// <summary>
    /// Exécution tick par tick. Appelé par le Behaviour Tree node.
    /// </summary>
    public void ExecutePlan()
    {
        if (_currentAction == null) return;

        if (!_currentAction.IsValid(_character))
        {
            _currentAction.Exit(_character);
            _currentAction = null;
            _currentPlan = null;
            return;
        }

        _currentAction.Execute(_character);

        if (_currentAction.IsComplete)
        {
            _currentAction.Exit(_character);
            if (_currentPlan != null && _currentPlan.Count > 0)
            {
                _currentAction = _currentPlan.Dequeue();
            }
            else
            {
                _currentAction = null;
                _currentPlan = null;
            }
        }
    }

    public void CancelPlan()
    {
        _currentAction?.Exit(_character);
        _currentAction = null;
        _currentPlan = null;
    }
}
