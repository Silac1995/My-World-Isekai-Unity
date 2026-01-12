using System;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]
public class Character : MonoBehaviour
{
    #region Serialized Fields
    [Header("Basic Info")]
    [SerializeField] private string _characterName;
    [SerializeField] private GenderType _startingGender;
    [SerializeField] private CharacterBio _characterBio;

    [Header("Stats & Race")]
    [SerializeField] private CharacterStats _stats;
    [SerializeField] private RaceSO _race;

    [Header("Components")]
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private CapsuleCollider _col;

    [Header("Sub-Systems")]
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;
    [SerializeField] private CharacterActions _characterActions;
    [SerializeField] private CharacterVisual _characterVisual;
    [SerializeField] private CharacterGameController _controller;
    [SerializeField] private CharacterEquipment _equipment;
    [SerializeField] private CharacterInteraction _characterInteraction;
    [SerializeField] private CharacterRelation _characterRelation;
    #endregion

    #region Private Fields
    private Transform _visualRoot;
    private GameObject _currentVisualInstance;
    private NavMeshAgent _cachedNavMeshAgent;
    private BattleManager _battleManager;
    private bool _isDead;

    // Ressources statiques partagées
    private static BattleManager _battleManagerPrefab;
    private static GameObject _worldItemPrefab;

    private const string BATTLE_MANAGER_PATH = "Prefabs/BattleManagerPrefab";
    private const string WORLD_ITEM_PATH = "Prefabs/WorldItem";
    private const float DROP_DISTANCE = 1.5f;
    #endregion

    #region Events
    public event Action<Character> OnDeath;
    #endregion

    #region Properties
    public string CharacterName { get => _characterName; set => _characterName = value; }
    public CharacterBio CharacterBio => _characterBio;
    public CharacterStats Stats => _stats ?? throw new NullReferenceException($"Stats manquantes sur {gameObject.name}");
    public RaceSO Race => _race;

    public float MovementSpeed => _stats?.MoveSpeed.CurrentValue ?? 0f;
    public Rigidbody Rigidbody => _rb;
    public CapsuleCollider Collider => _col;

    public CharacterGameController Controller => _controller;
    public CharacterVisual CharacterVisual => _characterVisual;
    public CharacterActions CharacterActions => _characterActions;
    public CharacterInteraction CharacterInteraction => _characterInteraction ?? throw new NullReferenceException($"CharacterInteraction non initialisé sur {gameObject.name}");
    public CharacterEquipment CharacterEquipment => _equipment;
    public CharacterRelation CharacterRelation => _characterRelation;

    public Transform VisualRoot => _visualRoot;
    public GameObject CurrentVisualInstance => _currentVisualInstance;
    public BattleManager BattleManager => _battleManager;
    #endregion

    #region Unity Lifecycle
    protected virtual void Awake()
    {
        if (!ValidateRequiredComponents()) return;

        // --- INITIALISATION DE LA BIO ---
        // Si la bio n'est pas déjà assignée (ou pour s'assurer que le Character est lié)
        if (_characterBio == null || _characterBio.Character == null)
        {
            // On utilise le constructeur qu'on a créé
            _characterBio = new CharacterBio(this, _startingGender, 1);
            Debug.Log($"<color=white>[Bio]</color> Bio initialisée pour {_characterName} ({_startingGender})");
        }

        LoadResources();
        _cachedNavMeshAgent = GetComponent<NavMeshAgent>();
        _isDead = false;
        InitializeRigidbody();
    }
    #endregion

    #region Initialization
    private void LoadResources()
    {
        if (_battleManagerPrefab == null)
            _battleManagerPrefab = Resources.Load<BattleManager>(BATTLE_MANAGER_PATH);

        if (_worldItemPrefab == null)
            _worldItemPrefab = Resources.Load<GameObject>(WORLD_ITEM_PATH);
    }

    private bool ValidateRequiredComponents()
    {
        if (_rb != null && _col != null) return true;

        Debug.LogError($"{name} : Références Rigidbody ou Collider manquantes !");
        enabled = false;
        return false;
    }

    private void InitializeRigidbody()
    {
        _rb.freezeRotation = true;
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    public void InitializeAll() => AdjustCapsuleCollider();

    public void InitializeStats(float health, float mana, float strength, float agility)
    {
        if (_stats == null) return;
        _stats.InitializeStats(health, mana, strength, agility);
    }

    public void InitializeRace(RaceSO raceData)
    {
        _race = raceData ?? throw new ArgumentNullException(nameof(raceData));
        Stats.MoveSpeed.IncreaseBaseValue(_race.bonusSpeed);

        if (_controller != null) _controller.Initialize();
    }
    #endregion

    #region Visuals & Collider
    public void AssignVisualRoot(Transform root) => _visualRoot = root;
    public void AssignVisualInstance(GameObject instance) => _currentVisualInstance = instance;

    private void AdjustCapsuleCollider()
    {
        if (_currentVisualInstance == null || _col == null) return;

        CapsuleCollider visualCol = _currentVisualInstance.GetComponentInChildren<CapsuleCollider>();
        if (visualCol == null) return;

        _col.center = visualCol.center;
        _col.radius = visualCol.radius;
        _col.height = visualCol.height;
        _col.direction = visualCol.direction;
        _col.isTrigger = visualCol.isTrigger;
        _col.material = visualCol.material;

        if (Application.isPlaying) Destroy(visualCol);
        else DestroyImmediate(visualCol);
    }
    #endregion

    #region Health & Status
    public bool IsAlive() => !_isDead;
    public bool IsPlayer() => _controller is PlayerController;
    public bool IsFree() => IsAlive() && !IsInBattle() && !_characterInteraction.IsInteracting;

    public virtual void Die()
    {
        if (_isDead) return;

        _isDead = true;
        OnDeath?.Invoke(this);

        if (_col != null) _col.enabled = false;
        if (_rb != null) _rb.isKinematic = true;

        if (_controller != null)
        {
            _controller.enabled = false;
            if (_controller.Animator != null) _controller.Animator.SetBool("isDead", true);
        }
    }

    public void TakeDamage(float damage = 1)
    {
        if (!IsAlive() || _stats == null) return;

        _stats.Health.CurrentAmount -= damage;
        if (_stats.Health.CurrentAmount <= 0) Die();
    }
    #endregion

    #region Battle Logic
    public bool IsInBattle() => _battleManager != null;

    public void JoinBattle(BattleManager manager) => _battleManager = manager;
    public void LeaveBattle() => _battleManager = null;

    public void StartFight(Character target)
    {
        if (!ValidateFight(target)) return;

        BattleManager manager = Instantiate(_battleManagerPrefab);

        BattleTeam team1 = new BattleTeam(); team1.AddCharacter(this);
        BattleTeam team2 = new BattleTeam(); team2.AddCharacter(target);

        manager.AddTeam(team1);
        manager.AddTeam(team2);
        manager.Initialize(this, target);

        JoinBattle(manager);
        target.JoinBattle(manager);
    }

    private bool ValidateFight(Character target)
    {
        return IsAlive() && target != null && target.IsAlive() && !IsInBattle() && !target.IsInBattle();
    }
    #endregion

    #region Context Switching (Player/NPC)
    public void SwitchToPlayer()
    {
        SwitchController<PlayerController>(GetComponent<NPCController>());
        SwitchInteractionDetector<PlayerInteractionDetector, NPCInteractionDetector>();
    }

    public void SwitchToNPC()
    {
        SwitchController<NPCController>(GetComponent<PlayerController>());
        SwitchInteractionDetector<NPCInteractionDetector, PlayerInteractionDetector>();
    }

    private void SwitchController<TTarget>(CharacterGameController toDisable) where TTarget : CharacterGameController
    {
        if (toDisable != null) toDisable.enabled = false;

        TTarget target = GetComponent<TTarget>();
        if (target != null)
        {
            target.enabled = true;
            target.Initialize();
            _controller = target;
        }

        bool isNPC = typeof(TTarget) == typeof(NPCController);
        if (_rb != null) _rb.isKinematic = isNPC;
        ConfigureNavMesh(isNPC);
    }

    private void ConfigureNavMesh(bool enabled)
    {
        if (_cachedNavMeshAgent == null) return;
        _cachedNavMeshAgent.enabled = enabled;
        if (enabled)
        {
            _cachedNavMeshAgent.updatePosition = true;
            _cachedNavMeshAgent.updateRotation = false;
        }
    }

    private void SwitchInteractionDetector<TTarget, TDisable>() where TTarget : MonoBehaviour where TDisable : MonoBehaviour
    {
        TDisable toDisable = GetComponent<TDisable>();
        if (toDisable != null) toDisable.enabled = false;

        TTarget target = GetComponent<TTarget>() ?? gameObject.AddComponent<TTarget>();
        (target as MonoBehaviour).enabled = true;
    }
    #endregion

    #region Inventory & Items
    public void DropItem(ItemInstance itemToDrop)
    {
        if (itemToDrop == null || _worldItemPrefab == null) return;

        // Utilisation de transform.right ou transform.up si tu es en 2D pure
        Vector3 dropPos = transform.position + (transform.up * DROP_DISTANCE);
        GameObject go = Instantiate(_worldItemPrefab, dropPos, Quaternion.identity);
        go.name = $"WorldItem_{itemToDrop.ItemSO.ItemName}";

        WorldItem worldItem = go.GetComponentInChildren<WorldItem>();
        if (worldItem != null) worldItem.Initialize(itemToDrop);

        // Correction : Utilisation du pattern matching direct
        if (itemToDrop is EquipmentInstance equipmentInstance)
        {
            // On vérifie les couleurs personnalisées
            if (equipmentInstance.HavePrimaryColor())
            {
                // Note : Pour tes sprites 2D, utilise SpriteRenderer au lieu de MeshRenderer
                SpriteRenderer visualRenderer = go.GetComponentInChildren<SpriteRenderer>();
                if (visualRenderer != null)
                    visualRenderer.color = equipmentInstance.PrimaryColor;
            }
        }
    }
    #endregion

    public void UseConsumable(ConsumableInstance consumable)
    {
        // TODO: Implémenter
    }

    public void EquipGear(EquipmentInstance equipment)
    {
        // TODO: Implémenter
    }

    #region Context Menus
    [ContextMenu("Take 50 Damage")] public void DebugTakeDamage() => TakeDamage(50f);
    [ContextMenu("Switch To Player")] public void DebugToPlayer() => SwitchToPlayer();
    [ContextMenu("Switch To NPC")] public void DebugToNPC() => SwitchToNPC();
    #endregion
}