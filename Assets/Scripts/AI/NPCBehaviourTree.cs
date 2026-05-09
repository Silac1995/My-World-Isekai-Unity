using UnityEngine;
using MWI.AI;

/// <summary>
/// Main MonoBehaviour for the NPC Behaviour Tree.
/// Builds the decision tree, maintains the blackboard, and ticks the tree each frame.
///
/// Priority tree:
/// 1. ORDERS (player/NPC)
/// 2. COMBAT (already in combat)
/// 3. ASSIST (friend in danger)
/// 4. AGGRESSION (enemy detected)
/// 5. NEEDS (hunger, social, clothing...)
/// 6. SCHEDULE (work, sleep...)
/// 7. SOCIAL (spontaneous socialization)
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

    // The condition nodes (kept as references for debug)
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
    private BTCond_IsInPartyFollow _partyFollowNode;

    [Header("Performance")]
    [SerializeField] [Tooltip("Temps en secondes entre chaque tick du Behaviour Tree.")]
    private float _tickIntervalSeconds = 0.1f; 

    private float _lastTickTime;

    public Blackboard Blackboard => _blackboard;
    public Character Character => _character;

    protected override void Awake()
    {
        base.Awake();

        // Initial stagger: each NPC starts its cycle at a slightly different time
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
            Debug.Log($"<color=lime>[BT]</color> {_character.CharacterName} : Behaviour Tree initialized.");
    }

    /// <summary>
    /// Builds the complete decision tree.
    /// The order of children in the Selector = the order of priority.
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
        _partyFollowNode = new BTCond_IsInPartyFollow();

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
            _orderNode,         // 1. Orders (max priority)
            _combatNode,        // 2. Active combat
            _entraideSequence,  // 3. Assist
            _agressionSequence, // 4. Aggression
            _partyFollowNode,   // 4.5 Party follow (member follows party leader)
            _punchOutNode,      // 5. Forced end of shift (Must punch out before going home)
            _scheduleNode,      // 5. Schedule (Work/Sleep > Personal Goals)
            _goapNode,          // 6. GOAP (Life Goals / Proactive)
            _socialNode,        // 8. Social
            _wanderNode         // 9. Wander (fallback)
        );
    }

    private bool _forceNextTick = false;

    /// <summary>
    /// Forces the BT to tick on the next frame, ignoring the stagger.
    /// Useful after an Unfreeze to avoid a visible delay.
    /// </summary>
    public void ForceNextTick() => _forceNextTick = true;

    private void Update()
    {
        if (!IsServer) return;
        if (!_isInitialized || _root == null) return;

        // Stagger based on time rather than frames to support high Time.timeScale (Fast-Forward).
        if (!_forceNextTick && Time.time < _lastTickTime + _tickIntervalSeconds) return;

        _lastTickTime = Time.time;
        _forceNextTick = false;

        // The NPC is not a player and must be alive
        if (_character.Controller is PlayerController) return;
        if (!_character.IsAlive()) return;

        // Pause the BT if the controller is frozen (interactions, cutscenes, etc.)
        if (_character.Controller != null && _character.Controller.IsFrozen) return;

        // Pause the BT during an interaction or during dialogue positioning (avoids micro-movements or pathing conflicts)
        if (_character.CharacterInteraction != null && (_character.CharacterInteraction.IsInteracting || _character.CharacterInteraction.IsPositioning)) return;

        // NEW: Pause the BT during an action (e.g. pick up, work, craft) so it is not interrupted by the schedule
        if (_character.CharacterActions != null && _character.CharacterActions.CurrentAction != null) return;

        // Tick the tree
        BTNodeStatus status = _root.Execute(_blackboard);

        // Debug display
        UpdateDebugNodeName();

        if (_debugLog)
        {
            Debug.Log($"[BT] {_currentNodeName}");
        }
    }

    // ========================================
    //  Public API for issuing orders
    // ========================================

    /// <summary>
    /// Gives an order to the NPC. The previous order is cancelled automatically.
    /// Can be called by the player, another NPC, or the system.
    /// </summary>
    public void GiveOrder(NPCOrder order)
    {
        if (!_isInitialized) Initialize();

        // Cancel the previous order if there is one
        NPCOrder currentOrder = _blackboard.Get<NPCOrder>(Blackboard.KEY_CURRENT_ORDER);
        if (currentOrder != null && !currentOrder.IsComplete)
        {
            currentOrder.Cancel(_character);
        }

        // NEW: Interrupt the current action to force the order (since the action was pausing the BT)
        if (_character.CharacterActions != null && _character.CharacterActions.CurrentAction != null)
        {
            _character.CharacterActions.ClearCurrentAction();
        }

        _blackboard.Set(Blackboard.KEY_CURRENT_ORDER, order);
        Debug.Log($"<color=magenta>[BT Order]</color> {_character.CharacterName} received order: {order.OrderType}");
    }

    /// <summary>
    /// Cancels the current order.
    /// </summary>
    public void CancelOrder()
    {
        NPCOrder currentOrder = _blackboard.Get<NPCOrder>(Blackboard.KEY_CURRENT_ORDER);
        if (currentOrder != null)
        {
            currentOrder.Cancel(_character);
            _blackboard.Remove(Blackboard.KEY_CURRENT_ORDER);
            Debug.Log($"<color=yellow>[BT Order]</color> Order cancelled for {_character.CharacterName}.");
        }
    }

    /// <summary>
    /// The currently active order (null if none).
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
        else if (_combatNode != null && _combatNode.IsRunning) _currentNodeName = "Combat";
        else if (_entraideSequence != null && _entraideSequence.IsRunning) _currentNodeName = "AssistFriend";
        else if (_agressionSequence != null && _agressionSequence.IsRunning) _currentNodeName = "Aggression";
        else if (_partyFollowNode != null && _partyFollowNode.IsRunning) _currentNodeName = "PartyFollow";
        else if (_punchOutNode != null && _punchOutNode.IsRunning) _currentNodeName = "PunchOut";
        else if (_scheduleNode != null && _scheduleNode.IsRunning) _currentNodeName = "Schedule";
        else if (_socialNode != null && _socialNode.IsRunning) _currentNodeName = "Social";
        else if (_goapNode != null && _goapNode.IsRunning) _currentNodeName = "GOAP";
        else if (_wanderNode != null && _wanderNode.IsRunning) _currentNodeName = "Wander";
        else _currentNodeName = "None";
    }
}
