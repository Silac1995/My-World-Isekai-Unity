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
public class NPCBehaviourTree : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Character _character;

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
    private BTCond_HasUrgentNeed _needsNode;
    private BTCond_HasScheduledActivity _scheduleNode;
    private BTCond_WantsToSocialize _socialNode;
    private BTAction_Wander _wanderNode;

    public Blackboard Blackboard => _blackboard;
    public Character Character => _character;

    private void Awake()
    {
        if (_character == null)
            _character = GetComponent<Character>();
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
        _orderNode = new BTCond_HasOrder();
        _combatNode = new BTCond_IsInCombat();
        _friendNode = new BTCond_FriendInDanger();
        _enemyNode = new BTCond_DetectedEnemy();
        _needsNode = new BTCond_HasUrgentNeed();
        _scheduleNode = new BTCond_HasScheduledActivity();
        _socialNode = new BTCond_WantsToSocialize();
        _wanderNode = new BTAction_Wander();

        return new BTSelector(
            _orderNode,         // 1. Ordres (priorité max)
            _combatNode,        // 2. Combat actif
            _friendNode,        // 3. Entraide
            _enemyNode,         // 4. Agression
            _needsNode,         // 5. Besoins
            _scheduleNode,      // 6. Schedule
            _socialNode,        // 7. Social
            _wanderNode         // 8. Wander (fallback)
        );
    }

    private void Update()
    {
        if (!_isInitialized || _root == null) return;

        // Le NPC n'est pas un joueur et doit être vivant
        if (_character.Controller is PlayerController) return;
        if (!_character.IsAlive()) return;

        // Tick l'arbre
        BTNodeStatus status = _root.Execute(_blackboard);

        // Debug display
        if (_debugLog)
        {
            UpdateDebugNodeName();
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

    private void UpdateDebugNodeName()
    {
        if (_orderNode.IsRunning) _currentNodeName = "Order";
        else if (_combatNode.IsRunning) _currentNodeName = "Combat";
        else if (_friendNode.IsRunning) _currentNodeName = "FriendInDanger";
        else if (_enemyNode.IsRunning) _currentNodeName = "DetectedEnemy";
        else if (_needsNode.IsRunning) _currentNodeName = "Needs";
        else if (_scheduleNode.IsRunning) _currentNodeName = "Schedule";
        else if (_socialNode.IsRunning) _currentNodeName = "Social";
        else if (_wanderNode.IsRunning) _currentNodeName = "Wander";
        else _currentNodeName = "None";
    }
}
