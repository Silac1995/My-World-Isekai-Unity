using UnityEngine;
using MWI.AI;

/// <summary>
/// MonoBehaviour principal du Behaviour Tree pour les NPCs.
/// Construit l'arbre de décision, maintient le blackboard, et tick l'arbre chaque frame.
/// 
/// Arbre de priorités :
/// 1. ORDRES (joueur/NPC)
/// 2. COMBAT (déjà en combat)
/// 3. ENTRAIDE (ami en danger)
/// 4. AGRESSION (ennemi détecté)
/// 5. BESOINS (faim, social, vêtements...)
/// 6. SCHEDULE (travail, sommeil...)
/// 7. SOCIAL (socialisation spontanée)
/// 8. WANDER (fallback)
/// </summary>
public class NPCBehaviourTree : CharacterSystem
{
    [Header("Debug")]
    [SerializeField] private bool _debugLog = false;
    [SerializeField] private string _currentNodeName = "None";

    private Blackboard _blackboard;
    private BTNode _root;
    private bool _isInitialized = false;

    // Les condition nodes (gardés en référence pour debug)
    private BTCond_HasOrder _orderNode;
    private BTCond_IsInCombat _combatNode;
    private BTCond_FriendInDanger _friendNode;
    private BTCond_DetectedEnemy _enemyNode;
    private BTCond_HasScheduledActivity _scheduleNode;
    private BTCond_WantsToSocialize _socialNode;
    private BTAction_ExecuteGoapPlan _goapNode;
    private BTAction_Wander _wanderNode;

    private BTSequence _legacySequence;
    private BTSequence _agressionSequence;
    private BTSequence _entraideSequence;
    private BTCond_NeedsToPunchOut _punchOutNode;

    [Header("Performance")]
    [SerializeField] [Tooltip("Temps en secondes entre chaque tick du Behaviour Tree.")]
    private float _tickIntervalSeconds = 0.1f; 

    private float _lastTickTime;

    public Blackboard Blackboard => _blackboard;
    public Character Character => _character;

    protected override void Awake()
    {
        base.Awake();

        // Stagger initial : chaque NPC commence son cycle à un moment légèrement différent
        _lastTickTime = UnityEngine.Time.time + (GetInstanceID() % 10) * 0.02f;
    }

    protected override void HandleIncapacitated(Character character)
    {
        CancelOrder();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (inCombat) CancelOrder();
    }

    private void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (_isInitialized) return;
        if (_character == null) return;

        _blackboard = new Blackboard(_character);
        _root = BuildTree();
        _isInitialized = true;

        if (_debugLog)
            Debug.Log($"<color=lime>[BT]</color> {_character.CharacterName} : Behaviour Tree initialisé.");
    }

    /// <summary>
    /// Construit l'arbre de décision complet.
    /// L'ordre des enfants dans le Selector = l'ordre de priorité.
    /// </summary>
    private BTNode BuildTree()
    {
        _legacySequence = new BTSequence(
            new BTCond_HasLegacyBehaviour(),
            new BTAction_ExecuteLegacyStack()
        );

        _orderNode = new BTCond_HasOrder();
        _combatNode = new BTCond_IsInCombat();
        _friendNode = new BTCond_FriendInDanger();
        _enemyNode = new BTCond_DetectedEnemy();
        _scheduleNode = new BTCond_HasScheduledActivity();
        _socialNode = new BTCond_WantsToSocialize();
        _goapNode = new BTAction_ExecuteGoapPlan();
        _wanderNode = new BTAction_Wander();
        _punchOutNode = new BTCond_NeedsToPunchOut();

        _entraideSequence = new BTSequence(
            _friendNode,
            new BTAction_AttackTarget()
        );

        _agressionSequence = new BTSequence(
            _enemyNode,
            new BTAction_AttackTarget()
        );

        return new BTSelector(
            _legacySequence,    // 0. Imperative actions bypass the intelligent tree
            _orderNode,         // 1. Ordres (priorité max)
            _combatNode,        // 2. Combat actif
            _entraideSequence,  // 3. Entraide
            _agressionSequence, // 4. Agression
            _punchOutNode,      // 4.5 Fin de shift forcé (Must punch out before going home)
            _scheduleNode,      // 5. Schedule (Work/Sleep > Personal Goals)
            _goapNode,          // 6. GOAP (Life Goals / Proactive)
            _socialNode,        // 8. Social
            _wanderNode         // 9. Wander (fallback)
        );
    }

    private bool _forceNextTick = false;

    /// <summary>
    /// Force le BT à ticker à la prochaine frame, en ignorant le stagger.
    /// Utile après un Unfreeze pour éviter un délai visible.
    /// </summary>
    public void ForceNextTick() => _forceNextTick = true;

    private void Update()
    {
        if (!IsServer) return;
        if (!_isInitialized || _root == null) return;

        // Stagger basé sur le temps plutôt que sur les frames pour supporter le Time.timeScale élevé (Fast-Forward).
        if (!_forceNextTick && Time.time < _lastTickTime + _tickIntervalSeconds) return;

        _lastTickTime = Time.time;
        _forceNextTick = false;

        // Le NPC n'est pas un joueur et doit être vivant
        if (_character.Controller is PlayerController) return;
        if (!_character.IsAlive()) return;

        // Pause le BT si le controller est gelé (interactions, cinématiques, etc.)
        if (_character.Controller != null && _character.Controller.IsFrozen) return;

        // Pause le BT pendant une interaction ou pendant le positionnement de dialogue (évite les micro-mouvements ou conflits de pathing)
        if (_character.CharacterInteraction != null && (_character.CharacterInteraction.IsInteracting || _character.CharacterInteraction.IsPositioning)) return;

        // NEW: Pause le BT pendant une action (ex: ramasser, travailler, crafter) pour ne pas être interrompu par l'emploi du temps
        if (_character.CharacterActions != null && _character.CharacterActions.CurrentAction != null) return;

        // Tick l'arbre
        BTNodeStatus status = _root.Execute(_blackboard);

        // Debug display
        UpdateDebugNodeName();

        if (_debugLog)
        {
            Debug.Log($"[BT] {_currentNodeName}");
        }
    }

    // ========================================
    //  API publique pour donner des ordres
    // ========================================

    /// <summary>
    /// Donne un ordre au NPC. L'ancien ordre est annulé automatiquement.
    /// Peut être appelé par le joueur, un autre NPC, ou le système.
    /// </summary>
    public void GiveOrder(NPCOrder order)
    {
        if (!_isInitialized) Initialize();

        // Annuler l'ancien ordre s'il y en a un
        NPCOrder currentOrder = _blackboard.Get<NPCOrder>(Blackboard.KEY_CURRENT_ORDER);
        if (currentOrder != null && !currentOrder.IsComplete)
        {
            currentOrder.Cancel(_character);
        }

        // NEW: Interrompre l'action en cours pour forcer l'ordre (puisque l'action mettait le BT en pause)
        if (_character.CharacterActions != null && _character.CharacterActions.CurrentAction != null)
        {
            _character.CharacterActions.ClearCurrentAction();
        }

        _blackboard.Set(Blackboard.KEY_CURRENT_ORDER, order);
        Debug.Log($"<color=magenta>[BT Order]</color> {_character.CharacterName} a reçu l'ordre : {order.OrderType}");
    }

    /// <summary>
    /// Annule l'ordre en cours.
    /// </summary>
    public void CancelOrder()
    {
        NPCOrder currentOrder = _blackboard.Get<NPCOrder>(Blackboard.KEY_CURRENT_ORDER);
        if (currentOrder != null)
        {
            currentOrder.Cancel(_character);
            _blackboard.Remove(Blackboard.KEY_CURRENT_ORDER);
            Debug.Log($"<color=yellow>[BT Order]</color> Ordre annulé pour {_character.CharacterName}.");
        }
    }

    /// <summary>
    /// L'ordre actuellement actif (null si aucun).
    /// </summary>
    public NPCOrder CurrentOrder => _blackboard?.Get<NPCOrder>(Blackboard.KEY_CURRENT_ORDER);

    // ========================================
    //  Debug
    // ========================================

    public string DebugCurrentNode => _currentNodeName;

    private void UpdateDebugNodeName()
    {
        if (_legacySequence != null && _legacySequence.IsRunning) _currentNodeName = "ImperativeStack";
        else if (_orderNode != null && _orderNode.IsRunning) _currentNodeName = "Order";
        else if (_punchOutNode != null && _punchOutNode.IsRunning) _currentNodeName = "PunchOut";
        else if (_combatNode != null && _combatNode.IsRunning) _currentNodeName = "Combat";
        else if (_entraideSequence != null && _entraideSequence.IsRunning) _currentNodeName = "AssistFriend";
        else if (_agressionSequence != null && _agressionSequence.IsRunning) _currentNodeName = "Aggression";
        else if (_scheduleNode != null && _scheduleNode.IsRunning) _currentNodeName = "Schedule";
        else if (_socialNode != null && _socialNode.IsRunning) _currentNodeName = "Social";
        else if (_goapNode != null && _goapNode.IsRunning) _currentNodeName = "GOAP";
        else if (_wanderNode != null && _wanderNode.IsRunning) _currentNodeName = "Wander";
        else _currentNodeName = "None";
    }
}
