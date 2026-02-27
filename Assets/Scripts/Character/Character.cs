using System;
using UnityEngine;
using UnityEngine.AI;
using MWI.Time;

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
    [SerializeField] private RigTypeSO rigType;

    [Header("Components")]
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private CapsuleCollider _col;

    [Header("Sub-Systems")]
    [SerializeField] private CharacterInteractable _characterInteractable;
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;
    [SerializeField] private CharacterActions _characterActions;
    [SerializeField] private CharacterMovement _characterMovement;
    [SerializeField] private CharacterVisual _characterVisual;
    [SerializeField] private CharacterGameController _controller;
    [SerializeField] private CharacterEquipment _equipment;
    [SerializeField] private CharacterInteraction _characterInteraction;
    [SerializeField] private CharacterRelation _characterRelation;
    [SerializeField] private CharacterCombat _characterCombat;
    [SerializeField] private CharacterNeeds _characterNeeds;
    [SerializeField] private CharacterAwareness _characterAwareness;
    [SerializeField] private CharacterSpeech _characterSpeech;
    [SerializeField] private CharacterStatusManager _statusManager;
    [SerializeField] private CharacterProfile _characterProfile;
    [SerializeField] private CharacterTraits _characterTraits;
    [SerializeField] private CharacterCommunity _characterCommunity;
    [SerializeField] private CharacterInvitation _characterInvitation;
    [SerializeField] private CharacterJob _characterJob;
    [SerializeField] private CharacterSchedule _characterSchedule;
    [SerializeField] private CharacterSkills _characterSkills;
    [SerializeField] private CharacterMentorship _characterMentorship;
    #endregion

    #region Private Fields
    private Transform _visualRoot;
    private GameObject _currentVisualInstance;
    private NavMeshAgent _cachedNavMeshAgent;
    private bool _isDead;
    private bool _isUnconscious;
    private TimeManager _timeManager;
    private CharacterParty _currentParty;

    // Ressources statiques partagées
    private static GameObject _worldItemPrefab;

    private const string BATTLE_MANAGER_PATH = "Prefabs/BattleManagerPrefab";
    private const string WORLD_ITEM_PATH = "Prefabs/WorldItem";
    private const float DROP_DISTANCE = 1.5f;
    #endregion

    #region Events
    public event Action<Character> OnDeath;
    public event Action<Character> OnIncapacitated;
    public event Action<bool> OnUnconsciousChanged;
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
    public CharacterMovement CharacterMovement => _characterMovement;
    public CharacterVisual CharacterVisual => _characterVisual;
    public CharacterActions CharacterActions => _characterActions;
    public CharacterInteraction CharacterInteraction => _characterInteraction ?? throw new NullReferenceException($"CharacterInteraction non initialisé sur {gameObject.name}");
    public CharacterEquipment CharacterEquipment => _equipment;
    public CharacterRelation CharacterRelation => _characterRelation;
    public CharacterParty CurrentParty => _currentParty;
    public CharacterCommunity CharacterCommunity => _characterCommunity;
    public CharacterInteractable CharacterInteractable => _characterInteractable;
    public CharacterCombat CharacterCombat => _characterCombat;
    public CharacterNeeds CharacterNeeds => _characterNeeds;
    public CharacterAwareness CharacterAwareness => _characterAwareness;
    public CharacterSpeech CharacterSpeech => _characterSpeech;
    public CharacterStatusManager StatusManager => _statusManager;
    public CharacterProfile CharacterProfile => _characterProfile;
    public CharacterTraits CharacterTraits => _characterTraits;
    public CharacterInvitation CharacterInvitation => _characterInvitation;
    public CharacterJob CharacterJob => _characterJob;
    public CharacterSchedule CharacterSchedule => _characterSchedule;
    public CharacterSkills CharacterSkills => _characterSkills;
    public CharacterMentorship CharacterMentorship => _characterMentorship;

    public NavMeshAgent NavMesh => _cachedNavMeshAgent;
    public TimeManager TimeManager => _timeManager != null ? _timeManager : TimeManager.Instance;

    public bool IsUnconscious => _isUnconscious;
    public bool IsIncapacitated => _isDead || _isUnconscious;
    public Transform VisualRoot => _visualRoot;
    public GameObject CurrentVisualInstance => _currentVisualInstance;
    public RigTypeSO RigType => rigType;
    #endregion


    void Update()
    {
        Shader.SetGlobalVector("_Body", _rb.position);
    }
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
        if (_characterMovement == null) _characterMovement = GetComponent<CharacterMovement>();
        if (_characterSpeech == null) _characterSpeech = GetComponentInChildren<CharacterSpeech>();
        if (_statusManager == null) _statusManager = GetComponent<CharacterStatusManager>();
        
        if (_characterProfile == null) _characterProfile = GetComponentInChildren<CharacterProfile>();
        if (_characterProfile != null) _characterProfile.Initialize(this);
        
        if (_characterTraits == null) _characterTraits = GetComponentInChildren<CharacterTraits>();
        if (_characterCommunity == null) _characterCommunity = GetComponentInChildren<CharacterCommunity>();
        if (_characterJob == null) _characterJob = GetComponentInChildren<CharacterJob>();
        if (_characterSchedule == null) _characterSchedule = GetComponentInChildren<CharacterSchedule>();
        if (_characterSkills == null) _characterSkills = GetComponent<CharacterSkills>();
        if (_characterMentorship == null) _characterMentorship = GetComponent<CharacterMentorship>();
        
        _cachedNavMeshAgent = GetComponent<NavMeshAgent>();
        _isDead = false;
        _isUnconscious = false;
    }
    #endregion

    #region Initialization
    private void LoadResources()
    {

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

    public void InitializeAll() => AdjustCapsuleCollider();

    public void InitializeStats(float health, float mana, float strength, float agility)
    {
        if (_stats == null) return;
        _stats.InitializeStats(health, mana, strength, agility);
    }

    public void InitializeRace(RaceSO raceData)
    {
        _race = raceData ?? throw new ArgumentNullException(nameof(raceData));
        
        // Applique dynamiquement toutes les stats (bases, offsets, multiplicateurs) depuis la race
        _stats.ApplyRaceStats(_race);

        if (_controller != null) _controller.Initialize();
    }
    #endregion

    #region Visuals & Collider
    public void AssignVisualRoot(Transform root) => _visualRoot = root;
    public void AssignVisualInstance(GameObject instance) => _currentVisualInstance = instance;

    private void AdjustCapsuleCollider()
    {
        if (_characterVisual == null || _col == null) return;

        // On exécute le calcul précis basé sur les sprites
        _characterVisual.ResizeColliderToSprite();

        // On s'assure que le Rigidbody n'est pas endormi pour appliquer le changement
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.WakeUp();
        }
    }
    #endregion

    #region Health & Status
    public bool IsAlive() => !_isDead && !_isUnconscious;
    public bool IsPlayer() => _controller is PlayerController;
    public bool IsFree() 
    {
        bool isTeaching = _characterMentorship != null && _characterMentorship.IsCurrentlyTeaching;
        return IsAlive() && !CharacterCombat.IsInBattle && !_characterInteraction.IsInteracting && !isTeaching;
    }

    #region Party Logic
    public bool IsInParty() => _currentParty != null;

    public bool IsPartyLeader()
    {
        return IsInParty() && _currentParty.IsLeader(this);
    }

    public void CreateParty(string partyName)
    {
        if (_currentParty != null)
        {
            _currentParty.RemoveMember(this);
        }
        _currentParty = new CharacterParty(partyName, this);
    }

    public void SetParty(CharacterParty party)
    {
        _currentParty = party;
    }

    public void Invite(Character target)
    {
        if (target == null || target == this) return;
        
        if (_currentParty == null)
        {
            CreateParty($"{_characterName}'s Group");
        }

        if (IsPartyLeader())
        {
            // Pour l'instant on l'ajoute directement (intégration avec les interactions à venir)
            _currentParty.AddMember(target);
        }
    }
    #endregion

    public virtual void SetUnconscious(bool unconscious)
    {
        HandleUnconsciousStatus(unconscious);
    }

    private void HandleUnconsciousStatus(bool unconscious)
    {
        if (_isDead || _isUnconscious == unconscious) return;

        if (unconscious && _characterCombat != null)
            _characterCombat.ExitCombatMode();

        _isUnconscious = unconscious;
        OnUnconsciousChanged?.Invoke(unconscious);

        if (unconscious)
        {
            // 1. Désactivation physique (partielle pour permettre le knockback)
            // On laisse le collider actif temporairement pour la physique de chute/recul
            if (_rb != null) _rb.isKinematic = true;

            // 2. Arrêt des systèmes actifs
            if (_characterMovement != null) _characterMovement.Stop();
            if (_characterActions != null) _characterActions.ClearCurrentAction();
            // Note : On ne retire plus le BattleManager ici pour permettre l'interaction/résurrection en combat.

            // 3. Désactivation du cerveau
            if (_controller != null)
            {
                _controller.ClearBehaviours();
                _controller.enabled = false;
            }

            // 4. Animation (Utilise le paramètre isDead pour le moment)
            if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
            {
                _characterVisual.CharacterAnimator.SetDead(true);
            }

            // 5. Désactivation NavMesh
            ConfigureNavMesh(false);

            Debug.Log($"<color=orange>[Status]</color> {CharacterName} est maintenant inconscient.");
            OnIncapacitated?.Invoke(this);
        }
        else
        {
            // --- RÉVEIL ---
            if (_col != null) _col.enabled = true;
            if (_rb != null) _rb.isKinematic = IsPlayer() ? false : true; // Rétablir selon le type de controller

            if (_controller != null)
            {
                _controller.enabled = true;
                _controller.Initialize(); // Repart sur Wander ou comportement par défaut
            }

            if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
            {
                _characterVisual.CharacterAnimator.SetDead(false);
            }

            // 5. Restauration NavMesh (Uniquement pour NPCs)
            ConfigureNavMesh(!IsPlayer());

            Debug.Log($"<color=orange>[Status]</color> {CharacterName} a repris connaissance.");
        }
    }

    public void WakeUp() => SetUnconscious(false);
    public void Faint() => SetUnconscious(true);

    public virtual void Die()
    {
        if (_isDead) return;

        if (_characterCombat != null)
            _characterCombat.ExitCombatMode();

        _isDead = true;
        _isUnconscious = false; // La mort prime sur l'inconscience
        OnDeath?.Invoke(this);

        // 1. Désactivation physique (partielle pour permettre le knockback)
        // Le collider sera désactivé via DisableColliderAfterKnockback dans CharacterMovement
        if (_rb != null) _rb.isKinematic = true;

        // 2. Arrêt des systèmes actifs
        if (_characterMovement != null) _characterMovement.Stop();
        if (_characterActions != null) _characterActions.ClearCurrentAction();
        if (_characterCombat != null) _characterCombat.ForceExitCombatMode();

        // 3. Désactivation du cerveau
        if (_controller != null)
        {
            _controller.ClearBehaviours();
            _controller.enabled = false;
        }

        if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
        {
            _characterVisual.CharacterAnimator.SetDead(true);
        }

        // 4. Désactivation NavMesh
        ConfigureNavMesh(false);

        OnIncapacitated?.Invoke(this);
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

    public void SetTimeManager(TimeManager manager)
    {
        _timeManager = manager;
    }

    #region Context Menus
    [ContextMenu("Take 50 Damage")] public void DebugTakeDamage() => CharacterCombat.TakeDamage(50f);
    [ContextMenu("Switch To Player")] public void DebugToPlayer() => SwitchToPlayer();
    [ContextMenu("Switch To NPC")] public void DebugToNPC() => SwitchToNPC();
    #endregion
}