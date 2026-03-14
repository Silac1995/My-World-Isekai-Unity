using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Contrôleur central pour le GOAP lié à l'individu (vie, besoins, objectifs personnels).
/// Contrairement au GOAP des Jobs (ex: JobGatherer), celui-ci gère les actions
/// permanentes et les buts de vie du personnage.
/// </summary>
public class CharacterGoapController : MonoBehaviour
{
    [SerializeField] private Character _character;
    
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

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    /// <summary>
    /// Met à jour le monde intérieur du NPC pour le planner.
    /// </summary>
    public void UpdateWorldState()
    {
        _worldState.Clear();
        
        // 1. Besoins de base
        if (_character.CharacterNeeds != null)
        {
            var needs = _character.CharacterNeeds.AllNeeds;
            foreach (var need in needs)
            {
                // On simplifie : un besoin est "satisfied" s'il n'est pas urgent
                string key = $"need_{need.GetType().Name.Replace("Need", "")}";
                _worldState[key] = !need.IsActive();
            }
        }

        // 2. État Professionnel
        bool hasJob = _character.CharacterJob != null && _character.CharacterJob.HasJob;
        _worldState["hasJob"] = hasJob;

        // 3. Connaissance locale (Sensors)
        // Note: Dans une version avancée, on checkerait ici si le personnage connaît un boss/building
        _worldState["knowsVacantJob"] = CheckForJobKnowledge();
        _worldState["atBossLocation"] = CheckAtBossLocation();
    }

    private bool CheckForJobKnowledge()
    {
        if (BuildingManager.Instance == null) return false;
        
        // Utiliser la méthode existante du BuildingManager pour trouver n'importe quel job vacant
        var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>();
        return building != null && building.HasOwner;
    }

    private bool CheckAtBossLocation()
    {
        if (_character.CharacterJob == null || _character.CharacterJob.HasJob) return false;
        
        var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>();
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
        
        // Définir les objectifs possibles (Priorités)
        List<GoapGoal> potentialGoals = new List<GoapGoal>();
        
        if (!_worldState["hasJob"])
        {
            potentialGoals.Add(new GoapGoal("FindJob", new Dictionary<string, bool> { { "hasJob", true } }, 10));
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

        // Prototype : Actions liées au travail
        if (BuildingManager.Instance == null) return actions;

        var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>();
        if (building != null && building.HasOwner && job != null)
        {
            actions.Add(new GoapAction_GoToBoss(building.Owner));
            actions.Add(new GoapAction_AskForJob(building, job));
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
