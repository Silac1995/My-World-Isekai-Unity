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

    [Header("Debug")]
    [SerializeField] private bool _debugLog = false;

    // État actuel
    private GoapGoal _currentGoal;
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;
    private Dictionary<string, bool> _worldState = new Dictionary<string, bool>();

    private float _lastReplanAttemptTime = -999f;

    // Scratch state, re-used between CheckForJobKnowledge / CheckAtBossLocation within a single Replan.
    private CommercialBuilding _cachedVacantJobBuilding;
    private bool _cachedJobLookupDone;

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

        // Invalidate the per-Replan job-lookup cache so we only hit BuildingManager once per Replan.
        _cachedJobLookupDone = false;
        _cachedVacantJobBuilding = null;

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
        _worldState["knowsVacantJob"] = CheckForJobKnowledge();
        _worldState["atBossLocation"] = CheckAtBossLocation();
    }

    private CommercialBuilding GetCachedVacantJobBuilding()
    {
        if (_cachedJobLookupDone) return _cachedVacantJobBuilding;
        _cachedJobLookupDone = true;

        if (BuildingManager.Instance == null) return null;

        var (building, _) = BuildingManager.Instance.FindAvailableJob<Job>(true);
        _cachedVacantJobBuilding = building;
        return building;
    }

    private bool CheckForJobKnowledge()
    {
        return GetCachedVacantJobBuilding() != null;
    }

    private bool CheckAtBossLocation()
    {
        if (_character.CharacterJob == null || _character.CharacterJob.HasJob) return false;

        var building = GetCachedVacantJobBuilding();
        if (building == null || !building.HasOwner) return false;

        float dist = Vector3.Distance(_character.transform.position, building.Owner.transform.position);
        return dist < 2.5f; // Distance d'interaction
    }

    /// <summary>
    /// Tente de trouver un plan pour satisfaire le but le plus urgent.
    /// Throttled by <see cref="_planReevaluationInterval"/>: if a previous attempt ran too recently,
    /// the call is a no-op. Returns whether a current plan is still active.
    /// </summary>
    public bool Replan()
    {
        // HOST perf: without this guard, every BT tick (0.1s) triggers up to 2 replans per NPC.
        // For jobless NPCs that bounces Replan→fail→Wander→re-enter GOAP at 20Hz, each firing 2×
        // FindAvailableJob scans (O(buildings) + LINQ shuffle) plus a factorial GOAP graph build.
        // Keep a single cadence: at most one attempt per `_planReevaluationInterval` seconds per NPC.
        //
        // Throttled calls are SILENT (no Debug.Log) even when `_debugLog` is true, because they fire
        // ~20×/sec per NPC and would flood the Windows console (the exact progressive-freeze pattern
        // this module is here to prevent). Only the actual, non-throttled attempt logs below.
        float now = UnityEngine.Time.time;
        if (now - _lastReplanAttemptTime < _planReevaluationInterval)
        {
            return _currentAction != null;
        }
        _lastReplanAttemptTime = now;

        if (_debugLog)
            Debug.Log($"<color=grey>[GOAP]</color> {_character.CharacterName}: Replan() running at t={now:F2}s (interval={_planReevaluationInterval}s).");

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

        // IMPORTANT: do NOT reset `_lastReplanAttemptTime` here.
        //
        // `BTAction_ExecuteGoapPlan.OnExit` calls CancelPlan every time the GOAP branch returns Failure —
        // which for a jobless NPC is every single BT tick (0.1s). Resetting the throttle here would
        // defeat the throttle entirely, reverting host-side CPU cost to the pre-fix 10 Replans/sec/NPC.
        //
        // If you need a specific transition (e.g., combat ends, NPC revives) to force an immediate
        // replan regardless of the throttle, call `ForceReplanNextTick()` explicitly at that site.
    }

    /// <summary>
    /// Allows the next BT tick to Replan immediately, bypassing <see cref="_planReevaluationInterval"/>.
    /// Use for rare, intent-driven transitions (combat-end, NPC revival, player dialogue choice) — never
    /// from internal BT OnExit paths, which would defeat the throttle.
    /// </summary>
    public void ForceReplanNextTick()
    {
        _lastReplanAttemptTime = -999f;
    }
}
