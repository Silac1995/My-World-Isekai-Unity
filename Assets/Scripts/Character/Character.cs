using System;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections.Generic;
using MWI.Time;
using MWI.AI;
using MWI.WorldSystem;

public enum CharacterBusyReason
{
    None,
    Dead,
    Unconscious,
    InCombat,
    Interacting,
    Crafting,
    Teaching,
    Building,
    DoingAction
}

[RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]

public class Character : NetworkBehaviour, MWI.Orders.IOrderIssuer
{
    #region Serialized Fields
    [Header("Archetype")]
    [SerializeField] private CharacterArchetype _archetype;

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
    [SerializeField] private CharacterTerrainEffects _terrainEffects;
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
    [SerializeField] private CharacterWallet _characterWallet;
    [SerializeField] private CharacterWorkLog _characterWorkLog;
    [SerializeField] private CharacterQuestLog _characterQuestLog;
    [SerializeField] private CharacterSchedule _characterSchedule;
    [SerializeField] private CharacterSkills _characterSkills;
    [SerializeField] private CharacterMentorship _characterMentorship;
    [SerializeField] private CharacterLocations _characterLocations;
    [SerializeField] private CharacterGoapController _characterGoap;
    [SerializeField] private CharacterCombatLevel _characterCombatLevel;
    [SerializeField] private CharacterBlueprints _characterBlueprints;
    [SerializeField] private CharacterAbilities _characterAbilities;
    [SerializeField] private CharacterBookKnowledge _characterBookKnowledge;
    [SerializeField] private BattleCircleManager _battleCircleManager;
    [SerializeField] private FloatingTextSpawner _floatingTextSpawner;
    [SerializeField] private CharacterAnimal _animal;
    [SerializeField] private CharacterParty _characterParty;
    [SerializeField] private MWI.Orders.CharacterOrders _characterOrders;
    [SerializeField] private FurniturePlacementManager _furniturePlacementManager;
    [SerializeField] private MWI.Farming.CropPlacementManager _cropPlacement;
    [SerializeField] private MWI.Cinematics.CharacterCinematicState _cinematicState;
    [SerializeField] private MWI.Ambition.CharacterAmbition _characterAmbition;
    #endregion

    #region Capability Registry
    // ── Capability Registry ──────────────────────────────────────────
    private readonly Dictionary<System.Type, CharacterSystem> _capabilitiesByType = new();
    private readonly List<CharacterSystem> _allCapabilities = new();

    /// <summary>Register a subsystem in the capability registry. Called by CharacterSystem.OnEnable.</summary>
    public void Register(CharacterSystem system)
    {
        if (system == null) return;
        var type = system.GetType();
        _capabilitiesByType[type] = system;
        if (!_allCapabilities.Contains(system))
            _allCapabilities.Add(system);
    }

    /// <summary>Unregister a subsystem from the capability registry. Called by CharacterSystem.OnDisable.</summary>
    public void Unregister(CharacterSystem system)
    {
        if (system == null) return;
        _capabilitiesByType.Remove(system.GetType());
        _allCapabilities.Remove(system);
    }

    /// <summary>Get a capability by exact type. Throws KeyNotFoundException if missing.</summary>
    public T Get<T>() where T : CharacterSystem
    {
        if (_capabilitiesByType.TryGetValue(typeof(T), out var system))
            return (T)system;
        throw new System.Collections.Generic.KeyNotFoundException(
            $"Capability {typeof(T).Name} not found on character '{CharacterName}'.");
    }

    /// <summary>Try to get a capability by exact type. Returns false if missing.</summary>
    public bool TryGet<T>(out T system) where T : CharacterSystem
    {
        if (_capabilitiesByType.TryGetValue(typeof(T), out var s))
        {
            system = (T)s;
            return true;
        }
        system = null;
        return false;
    }

    /// <summary>Check if a capability exists by exact type.</summary>
    public bool Has<T>() where T : CharacterSystem
    {
        return _capabilitiesByType.ContainsKey(typeof(T));
    }

    /// <summary>Get all capabilities implementing a given interface or base type. Linear scan.</summary>
    public System.Collections.Generic.IEnumerable<T> GetAll<T>()
    {
        for (int i = 0; i < _allCapabilities.Count; i++)
        {
            if (_allCapabilities[i] is T match)
                yield return match;
        }
    }
    #endregion

    #region IOrderIssuer
    // ── IOrderIssuer ─────────────────────────────────────────────────
    Character MWI.Orders.IOrderIssuer.AsCharacter => this;
    string    MWI.Orders.IOrderIssuer.DisplayName => CharacterName;
    ulong     MWI.Orders.IOrderIssuer.IssuerNetId => NetworkObject != null ? NetworkObject.NetworkObjectId : 0;
    #endregion

    #region Network Variables
    public NetworkVariable<Unity.Collections.FixedString64Bytes> NetworkRaceId = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<Unity.Collections.FixedString64Bytes> NetworkCharacterName = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> NetworkVisualSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<Unity.Collections.FixedString64Bytes> NetworkCharacterId = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Server-authoritative flag. True while the character is occupying a bed slot.
    /// Replicates to all peers so client visuals can switch to sleep pose; gates
    /// PlayerController.Update on the owning player. Not in ICharacterSaveData —
    /// sleeping characters wake up out-of-bed on save/load.
    /// </summary>
    public NetworkVariable<bool> NetworkIsSleeping = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Per-character target hours the player chose when entering the bed (1..168).
    /// Server-authoritative. The bed UseSlot path / UI_BedSleepPrompt sets this
    /// alongside <see cref="NetworkIsSleeping"/> = true. The TimeSkipController
    /// auto-trigger watcher reads this from every connected player and fires
    /// RequestSkip with hours = MIN(of all valid pending values) once every
    /// player is asleep. 0 = "no target chosen yet" (skip auto-trigger).
    /// Reset to 0 when the character ExitsSleep so a half-set state from a
    /// previous bed session can't accidentally re-trigger.
    /// </summary>
    public NetworkVariable<int> NetworkPendingSkipHours = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    #endregion

    #region Private Fields
    private Transform _visualRoot;
    private GameObject _currentVisualInstance;
    private NavMeshAgent _cachedNavMeshAgent;
    private bool _isDead;
    private bool _isUnconscious;
    private bool _isBuilding;
    private TimeManager _timeManager;
    private CharacterPathingMemory _pathingMemory;

    // Shared static resources
    private static GameObject _worldItemPrefab;

    private const string BATTLE_MANAGER_PATH = "Prefabs/BattleManagerPrefab";
    private const string WORLD_ITEM_PATH = "Prefabs/WorldItem";
    private const float DROP_DISTANCE = 1.5f;
    #endregion

    #region Events
    /// <summary>
    /// Fires on the server after any Character completes OnNetworkSpawn.
    /// Subscribers can use this to resolve references to newly-available characters
    /// (e.g., dormant relationships, pending party invitations).
    /// </summary>
    public static event Action<Character> OnCharacterSpawned;

    public event Action<Character> OnDeath;
    public event Action<Character> OnIncapacitated;
    public event Action<Character> OnWakeUp;
    public event Action<bool> OnUnconsciousChanged;
    public event System.Action<bool> OnSleepStateChanged;
    public event Action<bool> OnCombatStateChanged;
    public event Action<bool> OnBuildingStateChanged;
    #endregion

    #region Properties
    public CharacterArchetype Archetype => _archetype;
    public string CharacterName { get => _characterName; set => _characterName = value; }
    public CharacterBio CharacterBio => _characterBio;
    public CharacterStats Stats { get { var s = TryGet<CharacterStats>(out var reg) ? reg : _stats; return s ?? throw new NullReferenceException($"Stats manquantes sur {gameObject.name}"); } }
    public RaceSO Race => _race;

    public float MovementSpeed => _stats?.MoveSpeed.CurrentValue ?? 0f;
    public Rigidbody Rigidbody => _rb;
    public CapsuleCollider Collider => _col;

    public CharacterGameController Controller => TryGet<CharacterGameController>(out var s0) ? s0 : _controller;
    public CharacterMovement CharacterMovement => TryGet<CharacterMovement>(out var s1) ? s1 : _characterMovement;
    public CharacterVisual CharacterVisual => TryGet<CharacterVisual>(out var s2) ? s2 : _characterVisual;
    public CharacterActions CharacterActions => TryGet<CharacterActions>(out var s3) ? s3 : _characterActions;
    public CharacterInteraction CharacterInteraction { get { var s = TryGet<CharacterInteraction>(out var reg) ? reg : _characterInteraction; return s ?? throw new NullReferenceException($"CharacterInteraction not initialised on {gameObject.name}"); } }
    public CharacterEquipment CharacterEquipment => TryGet<CharacterEquipment>(out var s5) ? s5 : _equipment;
    public CharacterTerrainEffects TerrainEffects => TryGet<CharacterTerrainEffects>(out var ste) ? ste : _terrainEffects;
    public CharacterRelation CharacterRelation => TryGet<CharacterRelation>(out var s6) ? s6 : _characterRelation;
    public CharacterParty CharacterParty => TryGet<CharacterParty>(out var s7) ? s7 : _characterParty;
    public MWI.Orders.CharacterOrders CharacterOrders => TryGet<MWI.Orders.CharacterOrders>(out var sOrders) ? sOrders : _characterOrders;
    public CharacterCommunity CharacterCommunity => TryGet<CharacterCommunity>(out var s8) ? s8 : _characterCommunity;
    public CharacterInteractable CharacterInteractable => _characterInteractable;
    public CharacterCombat CharacterCombat => TryGet<CharacterCombat>(out var s10) ? s10 : _characterCombat;
    public CharacterNeeds CharacterNeeds => TryGet<CharacterNeeds>(out var s11) ? s11 : _characterNeeds;
    public CharacterAwareness CharacterAwareness => TryGet<CharacterAwareness>(out var s12) ? s12 : _characterAwareness;
    public CharacterSpeech CharacterSpeech => TryGet<CharacterSpeech>(out var s13) ? s13 : _characterSpeech;
    public CharacterStatusManager StatusManager => TryGet<CharacterStatusManager>(out var s14) ? s14 : _statusManager;
    public CharacterProfile CharacterProfile => TryGet<CharacterProfile>(out var s15) ? s15 : _characterProfile;
    public CharacterTraits CharacterTraits => TryGet<CharacterTraits>(out var s16) ? s16 : _characterTraits;
    public CharacterInvitation CharacterInvitation => TryGet<CharacterInvitation>(out var s17) ? s17 : _characterInvitation;
    public CharacterJob CharacterJob => TryGet<CharacterJob>(out var s18) ? s18 : _characterJob;
    public CharacterWallet CharacterWallet => TryGet<CharacterWallet>(out var sWallet) ? sWallet : _characterWallet;
    public CharacterWorkLog CharacterWorkLog => TryGet<CharacterWorkLog>(out var sWorkLog) ? sWorkLog : _characterWorkLog;
    public CharacterQuestLog CharacterQuestLog => TryGet<CharacterQuestLog>(out var sQuestLog) ? sQuestLog : _characterQuestLog;
    public CharacterSchedule CharacterSchedule => TryGet<CharacterSchedule>(out var s19) ? s19 : _characterSchedule;
    public CharacterSkills CharacterSkills => TryGet<CharacterSkills>(out var s20) ? s20 : _characterSkills;
    public CharacterMentorship CharacterMentorship => TryGet<CharacterMentorship>(out var s21) ? s21 : _characterMentorship;
    public CharacterLocations CharacterLocations => TryGet<CharacterLocations>(out var s22) ? s22 : _characterLocations;
    public CharacterGoapController CharacterGoap => TryGet<CharacterGoapController>(out var s23) ? s23 : _characterGoap;
    public CharacterCombatLevel CharacterCombatLevel => TryGet<CharacterCombatLevel>(out var s24) ? s24 : _characterCombatLevel;
    public CharacterBlueprints CharacterBlueprints => TryGet<CharacterBlueprints>(out var s25) ? s25 : _characterBlueprints;
    public CharacterAbilities CharacterAbilities => TryGet<CharacterAbilities>(out var s26) ? s26 : _characterAbilities;
    public CharacterBookKnowledge CharacterBookKnowledge => TryGet<CharacterBookKnowledge>(out var s27) ? s27 : _characterBookKnowledge;
    public BattleCircleManager BattleCircleManager => TryGet<BattleCircleManager>(out var s28) ? s28 : _battleCircleManager;
    public FloatingTextSpawner FloatingTextSpawner => TryGet<FloatingTextSpawner>(out var s29) ? s29 : _floatingTextSpawner;
    public CharacterAnimal CharacterAnimal => TryGet<CharacterAnimal>(out var sAnimal) ? sAnimal : _animal;
    public BuildingPlacementManager PlacementManager { get { var blueprints = TryGet<CharacterBlueprints>(out var reg) ? reg : _characterBlueprints; return blueprints != null ? blueprints.PlacementManager : null; } }
    public FurniturePlacementManager FurniturePlacementManager => TryGet<FurniturePlacementManager>(out var s31) ? s31 : _furniturePlacementManager;
    public MWI.Farming.CropPlacementManager CropPlacement => TryGet<MWI.Farming.CropPlacementManager>(out var s32) ? s32 : _cropPlacement;
    public MWI.Cinematics.CharacterCinematicState CharacterCinematicState => TryGet<MWI.Cinematics.CharacterCinematicState>(out var sCinema) ? sCinema : _cinematicState;
    public MWI.Ambition.CharacterAmbition CharacterAmbition =>
        TryGet<MWI.Ambition.CharacterAmbition>(out var sAmb) ? sAmb : _characterAmbition;
    public bool IsBuilding => _isBuilding;

    public NavMeshAgent NavMesh => _cachedNavMeshAgent;
    public TimeManager TimeManager => _timeManager != null ? _timeManager : TimeManager.Instance;
    public CharacterPathingMemory PathingMemory => _pathingMemory;

    public Furniture OccupyingFurniture { get; private set; }

    public bool IsUnconscious => _isUnconscious;
    public bool IsSleeping => NetworkIsSleeping.Value;

    /// <summary>
    /// Hours of sleep this player has chosen for the next time-skip cycle.
    /// 0 = no target set. Read by TimeSkipController's auto-trigger watcher
    /// to compute the closest target across all sleeping players.
    /// </summary>
    public int PendingSkipHours => NetworkPendingSkipHours.Value;
    public bool IsIncapacitated => _isDead || _isUnconscious;
    public Transform VisualRoot => _visualRoot;
    public GameObject CurrentVisualInstance => _currentVisualInstance;
    public RigTypeSO RigType => rigType;

    /// <summary>
    /// Persistent unique identifier for this character. Generated on first spawn, survives reconnects.
    /// </summary>
    public string CharacterId => NetworkCharacterId.Value.ToString();

    // ── Origin World ──────────────────────────────────────────────────
    private string _originWorldGuid;
    public string OriginWorldGuid
    {
        get => _originWorldGuid;
        set => _originWorldGuid = value;
    }

    // ── Abandoned NPC tracking — set when a party leader disconnects ──
    private bool _isAbandoned;
    public bool IsAbandoned
    {
        get => _isAbandoned;
        set => _isAbandoned = value;
    }

    private string _formerPartyLeaderId;
    public string FormerPartyLeaderId
    {
        get => _formerPartyLeaderId;
        set => _formerPartyLeaderId = value;
    }

    private string _formerPartyLeaderWorldGuid;
    public string FormerPartyLeaderWorldGuid
    {
        get => _formerPartyLeaderWorldGuid;
        set => _formerPartyLeaderWorldGuid = value;
    }
    #endregion


    /// <summary>
    /// Finds a spawned Character by its persistent UUID. Returns null if not found.
    /// </summary>
    public static Character FindByUUID(string uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return null;

        Character fallback = null;
        foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
        {
            if (c.CharacterId == uuid)
            {
                if (!c.IsAbandoned) return c;
                fallback = c;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Returns all abandoned characters whose former party leader matches the given ID.
    /// </summary>
    public static List<Character> FindAbandonedByFormerLeader(string formerLeaderId)
    {
        var results = new List<Character>();
        foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
        {
            if (c.IsAbandoned && c.FormerPartyLeaderId == formerLeaderId)
                results.Add(c);
        }
        return results;
    }

    void Update()
    {
        Shader.SetGlobalVector("_Body", _rb.position);

        // --- STRICT MULTIPLAYER PHYSICS LOCK ---
        // Prevents any local physics (like gravity) from fighting NetworkTransform on non-authoritative clients.
        // If another script accidentally changes isKinematic to false, this snaps it back immediately.
        if (IsSpawned && !IsOwner && !IsServer)
        {
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.isKinematic = true;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }
    }
    #region Unity Lifecycle
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Generate a persistent UUID on the server if not already set (e.g., from save data)
        if (IsServer && NetworkCharacterId.Value.IsEmpty)
        {
            NetworkCharacterId.Value = Guid.NewGuid().ToString("N");
        }

        // Stamp OriginWorldGuid from the current world on first spawn. Server-only; save-restore
        // overwrites with the saved value afterwards via CharacterDataCoordinator.Deserialize. If
        // SaveManager.CurrentWorldGuid isn't ready yet (early boot, tests), leave it empty — the
        // next spawn in a world context will fill it.
        if (IsServer && string.IsNullOrEmpty(_originWorldGuid))
        {
            string currentWorld = SaveManager.Instance != null ? SaveManager.Instance.CurrentWorldGuid : null;
            if (!string.IsNullOrEmpty(currentWorld))
            {
                _originWorldGuid = currentWorld;
            }
        }

        // Very important: IsOwner is true for the Host for ALL NPCs in the scene.
        // We only want the local client's specific avatar to become a "Player" with UI and Camera,
        // but ALL instances of a PlayerObject must use PlayerController logic.
        bool isPlayerObject = IsSpawned && NetworkObject.IsPlayerObject;
        bool isLocalOwner = IsSpawned && IsOwner;

        // The Host's player bypasses ConnectionApprovalCallback in many NGO versions.
        // The first frame of instantiation is chaotic (NavMeshAgent snaps, NetworkTransform initializes).
        // Therefore, we delay the teleport by exactly 1 frame so it successfully overrides everything cleanly.
        if (IsServer && NetworkObject.IsPlayerObject && OwnerClientId == NetworkManager.ServerClientId)
        {
            StartCoroutine(DelayedHostTeleport());
        }

        // Subscribe to name changes so late-joining clients always get the correct name.
        // On clients, the NetworkVariable value may not yet be populated at the exact
        // moment OnNetworkSpawn fires, so this callback ensures the name is applied
        // as soon as the server's value arrives (or on any subsequent rename).
        NetworkCharacterName.OnValueChanged += OnNetworkNameChanged;
        NetworkIsSleeping.OnValueChanged += HandleSleepStateChanged;

        // Apply the current value immediately if already available (normal case)
        if (!NetworkCharacterName.Value.IsEmpty)
        {
            _characterName = NetworkCharacterName.Value.ToString();
        }

        // LOAD CUSTOMIZATION FROM NETWORK VARIABLES
        RaceSO networkRace = null;
        if (!NetworkRaceId.Value.IsEmpty)
        {
            if (GameSessionManager.Instance != null)
                networkRace = GameSessionManager.Instance.GetRace(NetworkRaceId.Value.ToString());
            else
                Debug.LogWarning("[Character] GameSessionManager not found. Cannot fetch network race.");
        }

        if (SpawnManager.Instance != null)
        {
            // Fully initialize the character using the identical logic from SpawnManager
            SpawnManager.Instance.InitializeSpawnedCharacter(this, networkRace, isPlayerObject, isLocalOwner);
        }
        else
        {
            Debug.LogError("[Character] SpawnManager.Instance is null! Could not fully initialize network character.");
            if (isPlayerObject) SwitchToPlayer();
            else SwitchToNPC();
        }

        OnCharacterSpawned?.Invoke(this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        NetworkCharacterName.OnValueChanged -= OnNetworkNameChanged;
        NetworkIsSleeping.OnValueChanged -= HandleSleepStateChanged;
    }

    private void OnNetworkNameChanged(Unity.Collections.FixedString64Bytes previous, Unity.Collections.FixedString64Bytes current)
    {
        if (!current.IsEmpty)
        {
            _characterName = current.ToString();
            gameObject.name = _characterName;
            Debug.Log($"[Character] Name synced from network: {_characterName}");
        }
    }

    private void HandleSleepStateChanged(bool previous, bool current)
    {
        OnSleepStateChanged?.Invoke(current);
    }

    private System.Collections.IEnumerator DelayedHostTeleport()
    {
        yield return null; // Wait 1 frame
        
        if (SpawnManager.Instance != null && SpawnManager.Instance.DefaultSpawnPosition != Vector3.zero)
        {
            if (CharacterMovement != null) 
            {
                CharacterMovement.Warp(SpawnManager.Instance.DefaultSpawnPosition);
            }
            else 
            {
                transform.position = SpawnManager.Instance.DefaultSpawnPosition;
            }
            transform.rotation = SpawnManager.Instance.DefaultSpawnRotation;
            
            Debug.Log($"<color=green>[Host]</color> Teleported Host avatar to exactly {SpawnManager.Instance.DefaultSpawnPosition}");
        }
    }

    protected virtual void Awake()
    {
        if (!ValidateRequiredComponents()) return;

        // --- BIO INITIALISATION ---
        // If the bio isn't already assigned (or to make sure the Character is linked)
        if (_characterBio == null || _characterBio.Character == null)
        {
            // Use the constructor we created
            _characterBio = new CharacterBio(this, _startingGender, 1);
            Debug.Log($"<color=white>[Bio]</color> Bio initialised for {_characterName} ({_startingGender})");
        }

        LoadResources();
        if (_characterMovement == null) _characterMovement = GetComponent<CharacterMovement>();
        if (_characterSpeech == null) _characterSpeech = GetComponentInChildren<CharacterSpeech>();
        if (_statusManager == null) _statusManager = GetComponent<CharacterStatusManager>();
        
        if (_characterProfile == null) _characterProfile = GetComponentInChildren<CharacterProfile>();
        if (_characterProfile != null) _characterProfile.Initialize(this);
        
        if (_characterTraits == null) _characterTraits = GetComponentInChildren<CharacterTraits>();
        if (_characterCommunity == null) _characterCommunity = GetComponentInChildren<CharacterCommunity>();
        if (_characterInvitation == null) _characterInvitation = GetComponentInChildren<CharacterInvitation>();
        if (_characterJob == null) _characterJob = GetComponentInChildren<CharacterJob>();
        if (_characterSchedule == null) _characterSchedule = GetComponentInChildren<CharacterSchedule>();
        if (_characterSkills == null) _characterSkills = GetComponent<CharacterSkills>();
        if (_characterMentorship == null) _characterMentorship = GetComponent<CharacterMentorship>();
        if (_characterLocations == null) _characterLocations = GetComponent<CharacterLocations>();
        if (_characterGoap == null) _characterGoap = GetComponentInChildren<CharacterGoapController>();
        if (_characterCombatLevel == null) _characterCombatLevel = GetComponent<CharacterCombatLevel>();
        if (_characterBlueprints == null) _characterBlueprints = GetComponent<CharacterBlueprints>();
        if (_characterInteraction == null) _characterInteraction = GetComponent<CharacterInteraction>();
        if (_characterNeeds == null) _characterNeeds = GetComponent<CharacterNeeds>();
        if (_characterCombat == null) _characterCombat = GetComponent<CharacterCombat>();
        if (_characterRelation == null) _characterRelation = GetComponent<CharacterRelation>();
        if (_characterActions == null) _characterActions = GetComponent<CharacterActions>();
        if (_characterMovement == null) _characterMovement = GetComponent<CharacterMovement>();
        if (_characterVisual == null) _characterVisual = GetComponentInChildren<CharacterVisual>();
        if (_characterAwareness == null) _characterAwareness = GetComponentInChildren<CharacterAwareness>();
        if (_characterInteractable == null) _characterInteractable = GetComponentInChildren<CharacterInteractable>();
        if (_characterAbilities == null) _characterAbilities = GetComponent<CharacterAbilities>();
        if (_characterBookKnowledge == null) _characterBookKnowledge = GetComponent<CharacterBookKnowledge>();
        if (_battleCircleManager == null) _battleCircleManager = GetComponentInChildren<BattleCircleManager>();
        if (_floatingTextSpawner == null) _floatingTextSpawner = GetComponentInChildren<FloatingTextSpawner>();
        if (_animal == null) _animal = GetComponentInChildren<CharacterAnimal>();
        if (_characterParty == null) _characterParty = GetComponentInChildren<CharacterParty>();
        if (_furniturePlacementManager == null) _furniturePlacementManager = GetComponentInChildren<FurniturePlacementManager>();
        if (_characterOrders == null) _characterOrders = GetComponentInChildren<MWI.Orders.CharacterOrders>();
        if (_terrainEffects == null) _terrainEffects = GetComponentInChildren<CharacterTerrainEffects>();
        if (_characterAmbition == null)
            _characterAmbition = GetComponentInChildren<MWI.Ambition.CharacterAmbition>();

        _cachedNavMeshAgent = GetComponent<NavMeshAgent>();
        _isDead = false;
        _isUnconscious = false;
        _pathingMemory = new CharacterPathingMemory(this);
    }

    protected virtual void OnDestroy()
    {
        _pathingMemory?.CleanUp();
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

        Debug.LogError($"{name} : missing Rigidbody or Collider references!");
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

        if (string.IsNullOrEmpty(_characterName) && _race.NameGenerator != null)
        {
            _characterName = _race.NameGenerator.GenerateName(_startingGender);
            Debug.Log($"<color=cyan>[NameGenerator]</color> Named a new {_race.RaceName}: {_characterName}");
        }

        if (_controller != null) _controller.Initialize();
    }
    #endregion

    #region Visuals & Collider
    public void AssignVisualRoot(Transform root) => _visualRoot = root;
    public void AssignVisualInstance(GameObject instance) => _currentVisualInstance = instance;

    private void AdjustCapsuleCollider()
    {
        if (_characterVisual == null || _col == null) return;

        // Run the precise sprite-based calculation
        _characterVisual.ResizeColliderToSprite();

        // Make sure the Rigidbody isn't asleep so the change is applied
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.WakeUp();
        }
    }
    #endregion

    #region Health & Status
    public bool IsAlive() => !_isDead && !_isUnconscious;
    public bool IsPlayer() => _controller is PlayerController;

    public CharacterBusyReason BusyReason
    {
        get
        {
            IsFree(out CharacterBusyReason reason);
            return reason;
        }
    }

    public bool IsFree(out CharacterBusyReason reason)
    {
        if (_isDead)
        {
            reason = CharacterBusyReason.Dead;
            return false;
        }

        if (_isUnconscious)
        {
            reason = CharacterBusyReason.Unconscious;
            return false;
        }

        if (CharacterCombat != null && CharacterCombat.IsInBattle)
        {
            reason = CharacterBusyReason.InCombat;
            return false;
        }

        if (_characterInteraction != null && _characterInteraction.IsInteracting)
        {
            reason = CharacterBusyReason.Interacting;
            return false;
        }

        if (_isBuilding)
        {
            reason = CharacterBusyReason.Building;
            return false;
        }

        if (_characterMentorship != null && _characterMentorship.IsCurrentlyTeaching)
        {
            reason = CharacterBusyReason.Teaching;
            return false;
        }

        if (_characterActions != null && _characterActions.CurrentAction != null)
        {
            // Allow CharacterStartInteraction to bypass IsFree because it invokes StartInteractionWith itself
            if (!(_characterActions.CurrentAction is CharacterStartInteraction))
            {
                reason = _characterActions.CurrentAction is CharacterCraftAction ? CharacterBusyReason.Crafting : CharacterBusyReason.DoingAction;
                return false;
            }
        }

        reason = CharacterBusyReason.None;
        return true;
    }

    public bool IsFree() 
    {
        return IsFree(out _);
    }

    public void SetBuildingState(bool active)
    {
        if (_isBuilding == active) return;
        _isBuilding = active;
        OnBuildingStateChanged?.Invoke(_isBuilding);
    }

    #region Party Logic
    public bool IsInParty() => _characterParty != null && _characterParty.IsInParty;

    public bool IsPartyLeader() => _characterParty != null && _characterParty.IsPartyLeader;
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
            // 1. Physics deactivation
            if (_rb != null)
            {
                _rb.isKinematic = true;
                if (TryGetComponent<Unity.Netcode.Components.NetworkRigidbody>(out var netRb)) netRb.enabled = false;
            }

            // If the character isn't flying through the air from a knockback, disable the ground collider immediately
            if (_characterMovement != null && !_characterMovement.IsKnockedBack)
            {
                if (_col != null) _col.enabled = false;
                if (_rb != null) _rb.useGravity = false;
            }

            // 4. Animation (uses the isDead parameter for now)
            if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
            {
                _characterVisual.CharacterAnimator.SetDead(true);
            }

            // 5. NavMesh deactivation
            ConfigureNavMesh(false);

            Debug.Log($"<color=orange>[Status]</color> {CharacterName} is now unconscious.");
            OnIncapacitated?.Invoke(this);
        }
        else
        {
            // --- WAKE UP ---
            if (_col != null) _col.enabled = true;
            if (_rb != null)
            {
                _rb.useGravity = true;
                _rb.isKinematic = IsPlayer() ? false : true; // Restore depending on the controller type
                if (TryGetComponent<Unity.Netcode.Components.NetworkRigidbody>(out var netRb)) netRb.enabled = IsPlayer();
            }

            if (_controller != null)
            {
                _controller.enabled = true;
                _controller.Initialize(); // Restart on Wander or default behaviour
            }

            if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
            {
                _characterVisual.CharacterAnimator.SetDead(false);
            }

            // 5. NavMesh restoration (NPCs only)
            ConfigureNavMesh(!IsPlayer());

            Debug.Log($"<color=orange>[Status]</color> {CharacterName} regained consciousness.");
            OnWakeUp?.Invoke(this);
        }
    }

    public void WakeUp() => SetUnconscious(false);
    public void Faint() => SetUnconscious(true);

    public void SetCombatState(bool inCombat)
    {
        OnCombatStateChanged?.Invoke(inCombat);
    }

    public virtual void Die()
    {
        if (_isDead) return;

        if (_characterCombat != null)
            _characterCombat.ExitCombatMode();

        _isDead = true;
        _isUnconscious = false; // Death takes priority over unconsciousness
        OnDeath?.Invoke(this);

        // 1. Physics deactivation
        if (_rb != null)
        {
            _rb.isKinematic = true;
            if (TryGetComponent<Unity.Netcode.Components.NetworkRigidbody>(out var netRb)) netRb.enabled = false;
        }

        // The collider will be disabled by CharacterMovement if we suffer a knockback.
        // Otherwise, disable it immediately so we don't block other players (dead body)
        if (_characterMovement != null && !_characterMovement.IsKnockedBack)
        {
            if (_col != null) _col.enabled = false;
            if (_rb != null) _rb.useGravity = false;
        }

        if (_characterCombat != null) _characterCombat.ForceExitCombatMode();

        if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
        {
            _characterVisual.CharacterAnimator.SetDead(true);
        }

        // 4. NavMesh deactivation
        ConfigureNavMesh(false);

        OnIncapacitated?.Invoke(this);
    }

    #endregion

    #region Context Switching (Player/NPC)
    public void SwitchToPlayer()
    {
        SwitchController<PlayerController>(GetComponent<NPCController>());
        SwitchInteractionDetector<PlayerInteractionDetector, NPCInteractionDetector>();

        if (IsSpawned && IsOwner)
        {
            CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
            if (cameraFollow != null) cameraFollow.SetGameObject(gameObject);

            // Link with the centralized HUD
            PlayerUI playerUI = UnityEngine.Object.FindAnyObjectByType<PlayerUI>(FindObjectsInactive.Include);
            if (playerUI != null) playerUI.Initialize(gameObject);
        }
    }

    public void SwitchToNPC()
    {
        SwitchController<NPCController>(GetComponent<PlayerController>());
        SwitchInteractionDetector<NPCInteractionDetector, PlayerInteractionDetector>();
        
        if (CharacterEquipment != null)
        {
            CharacterEquipment.ClearNotifications();
        }
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
        ConfigureNavMesh(isNPC);
        
        if (TryGetComponent<Unity.Netcode.Components.NetworkRigidbody>(out var netRb))
        {
            netRb.enabled = !isNPC;
        }

        if (TryGetComponent<Unity.Netcode.Components.NetworkTransform>(out var netTransform))
        {
            // Always sync Y so late-joining clients receive the correct height.
            // For NPCs, raise the threshold to filter out NavMeshAgent micro-adjustments
            // that cause visible vertical wobble on clients.
            netTransform.SyncPositionY = true;
            if (isNPC)
                netTransform.PositionThreshold = 0.4f;
        }

        if (_rb != null)
        {
            if (IsSpawned && !IsOwner)
            {
                _rb.isKinematic = true;
            }
            else
            {
                _rb.isKinematic = isNPC;
            }
        }
    }

    // Stores the original interpolation mode so we can restore it when returning to Player control
    private RigidbodyInterpolation _savedInterpolation = RigidbodyInterpolation.None;

    public void ConfigureNavMesh(bool enabled)
    {
        if (_cachedNavMeshAgent == null) return;
        
        if (enabled)
        {
            // 1. Lock physics BEFORE enabling the agent
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                
                // CRITICAL FIX: Interpolation on a kinematic rigidbody fights the NavMeshAgent's translation,
                // causing extreme stutter and slow movement. We must disable it while the agent controls the transform.
                _savedInterpolation = _rb.interpolation;
                _rb.interpolation = RigidbodyInterpolation.None;
            }

            // 2. Enable and configure agent
            bool shouldEnableAgent = true;
            if (IsSpawned && !IsServer && !IsOwner)
            {
                shouldEnableAgent = false;
            }

            if (shouldEnableAgent)
            {
                _cachedNavMeshAgent.enabled = true;
                if (_cachedNavMeshAgent.isOnNavMesh) _cachedNavMeshAgent.isStopped = false;
                _cachedNavMeshAgent.updatePosition = true;
                _cachedNavMeshAgent.updateRotation = false;
                
                // Centrally enforce snappy movement and let visuals handle rotation via flipping
                _cachedNavMeshAgent.acceleration = 50f;
                _cachedNavMeshAgent.angularSpeed = 0f;
            }
            else
            {
                _cachedNavMeshAgent.enabled = false;
            }
        }
        else
        {
            // 1. Stop and disable agent BEFORE unlocking physics
            if (_cachedNavMeshAgent.isOnNavMesh)
            {
                _cachedNavMeshAgent.isStopped = true;
                _cachedNavMeshAgent.ResetPath();
            }
            _cachedNavMeshAgent.enabled = false;

            // 2. Unlock physics
            if (_rb != null && _controller is PlayerController) // Keep NPCs kinematic
            {
                if (!IsSpawned || IsOwner)
                {
                    _rb.isKinematic = false;
                    _rb.interpolation = _savedInterpolation; // Restore smooth WASD movement
                }
            }
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

        // Fix: direct pattern matching
        if (itemToDrop is EquipmentInstance equipmentInstance)
        {
            // Check the customised colours
            if (equipmentInstance.HavePrimaryColor())
            {
                // Note: for 2D sprites, use SpriteRenderer instead of MeshRenderer
                SpriteRenderer visualRenderer = go.GetComponentInChildren<SpriteRenderer>();
                if (visualRenderer != null)
                    visualRenderer.color = equipmentInstance.PrimaryColor;
            }
        }
    }
    #endregion

    public void UseConsumable(ConsumableInstance consumable)
    {
        if (consumable == null)
        {
            Debug.LogWarning($"<color=orange>[Character]</color> {CharacterName} UseConsumable called with null instance.");
            return;
        }

        try
        {
            consumable.ApplyEffect(this);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return;
        }

        var so = consumable.ConsumableData;
        if (so == null || !so.DestroyOnUse) return;

        // Remove the item from wherever it lives (hands and/or inventory).
        var hands = CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.IsCarrying && hands.CarriedItem == consumable)
        {
            hands.ClearCarriedItem();
        }

        if (CharacterEquipment != null && CharacterEquipment.HaveInventory())
        {
            CharacterEquipment.GetInventory().RemoveItem(consumable, this);
        }
    }

    public void EquipGear(EquipmentInstance equipment)
    {
        // TODO: Implement
    }

    public void SetTimeManager(TimeManager manager)
    {
        _timeManager = manager;
    }

    #region Context Menus
    [ContextMenu("Take 50 Damage")] public void DebugTakeDamage() => CharacterCombat.TakeDamage(50f);
    [ContextMenu("Switch To Player")] public void DebugToPlayer() => SwitchToPlayer();
    [ContextMenu("Switch To NPC")] public void DebugToNPC() => SwitchToNPC();
    
    [Header("Dialogue Test")]
    [SerializeField] private MWI.Dialogue.DialogueSO _testDialogue;
    [ContextMenu("Start Test Dialogue")]
    public void DebugStartDialogue()
    {
        var manager = GetComponent<DialogueManager>() ?? gameObject.AddComponent<DialogueManager>();
        
        // Find other characters to act as participants
        List<Character> participants = new List<Character> { this };
        Character[] allCharacters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        
        foreach (var c in allCharacters)
        {
            if (c != this) participants.Add(c);
            if (participants.Count >= 3) break; // Test with up to 3
        }
        
        manager.StartDialogue(_testDialogue, participants);
    }
    #endregion

    public void SetOccupyingFurniture(Furniture furniture)
    {
        OccupyingFurniture = furniture;
    }

    /// <summary>
    /// Server-only. Snap the character to the given anchor, disable navigation,
    /// and flip <see cref="NetworkIsSleeping"/> to true. Called by
    /// <see cref="BedFurniture.UseSlot"/>. Position is server-driven — clients
    /// receive the snap via NetworkTransform, not via this method.
    /// </summary>
    public void EnterSleep(Transform anchor)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"<color=orange>[Character]</color> EnterSleep called on non-server peer for {CharacterName}. Ignored.");
            return;
        }
        if (anchor == null)
        {
            Debug.LogError($"<color=red>[Character]</color> EnterSleep called with null anchor for {CharacterName}.");
            return;
        }
        if (NetworkIsSleeping.Value)
        {
            Debug.LogWarning($"<color=orange>[Character]</color> EnterSleep on {CharacterName} but already sleeping. Ignored.");
            return;
        }

        transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        // ConfigureNavMesh(false) zeroes isStopped, calls ResetPath(), then disables the agent
        // and unlocks physics in the correct order. Mirrors HandleUnconsciousStatus.
        ConfigureNavMesh(false);
        NetworkIsSleeping.Value = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"<color=cyan>[Character]</color> {CharacterName} EnterSleep at {anchor.name} ({anchor.position}).");
#endif
    }

    /// <summary>
    /// Server-only. Re-enable navigation and flip <see cref="NetworkIsSleeping"/> to false.
    /// The character stays at the anchor's last position; the next AI tick or player input
    /// drives them away. Called by <see cref="BedFurniture.ReleaseSlot"/>.
    /// </summary>
    public void ExitSleep()
    {
        if (!IsServer)
        {
            Debug.LogWarning($"<color=orange>[Character]</color> ExitSleep called on non-server peer for {CharacterName}. Ignored.");
            return;
        }
        if (!NetworkIsSleeping.Value)
        {
            // No-op on idempotent call; common during shutdown.
            return;
        }

        // ConfigureNavMesh(true) zeroes velocity / forces kinematic BEFORE enabling the agent
        // and skips enable on non-server non-owner clients. Players don't drive a NavMeshAgent,
        // so mirror the !IsPlayer() gate used by HandleUnconsciousStatus on wake.
        ConfigureNavMesh(!IsPlayer());
        NetworkIsSleeping.Value = false;
        // Reset the per-skip target so a stale value from this sleep session
        // can't auto-fire the next skip when the player returns to bed.
        NetworkPendingSkipHours.Value = 0;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"<color=cyan>[Character]</color> {CharacterName} ExitSleep.");
#endif
    }

    /// <summary>
    /// Server-only. Sets the player's chosen skip duration for the upcoming
    /// time-skip cycle. Called by the bed UseSlot path / UI_BedSleepPrompt
    /// alongside (or just before) <see cref="EnterSleep"/>. Clamped to
    /// [1, MWI.Time.TimeSkipController.MaxHours]. Replicates to all peers via
    /// <see cref="NetworkPendingSkipHours"/> so the auto-trigger watcher can
    /// read it from any server-side iteration of player Characters.
    /// </summary>
    public void SetPendingSkipHours(int hours)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"<color=orange>[Character]</color> SetPendingSkipHours called on non-server peer for {CharacterName}. Ignored.");
            return;
        }
        if (hours < 1) hours = 0;  // 0 = "no target set" (auto-trigger ignores)
        else if (hours > MWI.Time.TimeSkipController.MaxHours) hours = MWI.Time.TimeSkipController.MaxHours;
        NetworkPendingSkipHours.Value = hours;
    }
}