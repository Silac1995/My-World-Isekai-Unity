using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using MWI.WorldSystem;
using System;
using Unity.Collections;

/// <summary>
/// Classe abstraite mère de tous les bâtiments.
/// Hérite de Zone pour bénéficier du trigger, du NavMesh sampling, et du tracking des personnages.
/// </summary>
public class Building : ComplexRoom
{
    /// <summary>
    /// One entry of <see cref="_defaultFurnitureLayout"/> — a furniture item the server spawns
    /// as a child of the building when the building first comes into existence in a fresh world
    /// (i.e. via <c>BuildingPlacementManager</c>, NOT via save-restore).
    ///
    /// Use this — NOT nested PrefabInstances inside the building prefab — for any furniture whose
    /// prefab carries a NetworkObject. Nesting a network-bearing furniture instance inside a
    /// runtime-spawned building prefab makes NGO half-register the child during the parent's
    /// spawn, leaving a broken NetworkObject in <c>SpawnManager.SpawnedObjectsList</c> that
    /// NRE's during the next client-sync (see wiki/gotchas/host-progressive-freeze-debug-log-spam.md
    /// neighbouring entries on half-spawned NOs, and .agent/skills/multiplayer/SKILL.md §10).
    ///
    /// Available on every <see cref="Building"/> subclass — Residential, Commercial, Harvesting,
    /// Transporter, etc. Hoisted from <c>CommercialBuilding</c> on 2026-05-01.
    /// </summary>
    [System.Serializable]
    public class DefaultFurnitureSlot
    {
        [Tooltip("FurnitureItemSO whose InstalledFurniturePrefab will be Instantiate+Spawn'd as a child of the building.")]
        public FurnitureItemSO ItemSO;

        [Tooltip("Position relative to the building root, in building-local space. Used to compute the world spawn position; the room's FurnitureGrid then snaps + occupies the matching cell.")]
        public Vector3 LocalPosition;

        [Tooltip("Rotation relative to the building root, in building-local space (Euler degrees).")]
        public Vector3 LocalEulerAngles;

        [Tooltip("Room whose FurnitureManager owns and grid-registers this furniture. Mirror of the legacy nested-prefab parenting (e.g. Forge/Room_Main/CraftingStation → set TargetRoom to Room_Main). Required for any furniture that should appear on a room's FurnitureGrid; if left null the furniture spawns parented directly under the building root and is NOT grid-registered.")]
        public Room TargetRoom;
    }

    [Header("Building Info")]
    [SerializeField] protected string buildingName;

    [SerializeField] protected bool _isPublicLocation = false;

    [SerializeField] protected BuildingType _buildingType = BuildingType.Residential; // Default value

    [SerializeField] protected Collider _buildingZone;

    [Header("Logistics Phase")]
    [SerializeField] protected Zone _deliveryZone;

    [Header("Construction")]
    [SerializeField] protected List<CraftingIngredient> _constructionRequirements = new List<CraftingIngredient>();
    protected Dictionary<ItemSO, int> _contributedMaterials = new Dictionary<ItemSO, int>();

    [Tooltip("Tick on scene-authored / pre-placed buildings that should spawn already Complete (skip the construction loop). Has no effect on runtime placements via BuildingPlacementManager — those always go through UnderConstruction → Complete unless InstantMode is on. Save/load takes precedence over this flag — restored buildings keep whatever state they had when saved.")]
    [SerializeField] protected bool _spawnAsComplete = false;

    [Tooltip("Child GameObject holding the scaffolding renderers/colliders shown while UnderConstruction. Active iff CurrentState == UnderConstruction.")]
    [SerializeField] protected GameObject _constructionVisualRoot;

    [Tooltip("Child GameObject holding the finished-building renderers/colliders shown after Complete. Active iff CurrentState == Complete.")]
    [SerializeField] protected GameObject _completedVisualRoot;

    [Tooltip("Translucent material applied to a flat footprint quad on the ground at the BuildingZone bounds. The ONLY visible element under ConstructionVisual while UnderConstruction — gives players a clear drop-zone marker. Leave null to skip the marker entirely.")]
    [SerializeField] protected Material _constructionFootprintMaterial;

    [Tooltip("Particle material (URP/Particles/Unlit recommended) applied to the upward-rising 'curtain' barrier ParticleSystem auto-spawned at the footprint perimeter. Particles fade alpha bottom→top over their lifetime to simulate a translucent barrier wall. Leave null to skip the curtain effect.")]
    [SerializeField] protected Material _constructionCurtainMaterial;

    /// <summary>
    /// Set after <see cref="EnsureConstructionGhostVisual"/> populates _constructionVisualRoot
    /// from a clone of _completedVisualRoot. Per-instance so re-entering / re-spawning a
    /// building doesn't re-clone over the existing children.
    /// </summary>
    private bool _ghostVisualPopulated;

    /// <summary>
    /// Renderers on top-level Building children OUTSIDE _completedVisualRoot (e.g.,
    /// Interior Door wall meshes) that we cloned into the ghost set. We toggle their
    /// `.enabled` flag in <see cref="ApplyConstructionVisuals"/> so the original
    /// opaque mesh hides while UnderConstruction without disabling the host
    /// GameObject's scripts/network components.
    /// </summary>
    private readonly System.Collections.Generic.List<Renderer> _extraOriginalRenderersToToggle =
        new System.Collections.Generic.List<Renderer>();

    /// <summary>
    /// Colliders on the same extra children. While UnderConstruction we disable them
    /// alongside the renderers so the player can walk freely through the footprint
    /// (e.g., the Interior Door's wall doesn't block the character before the building
    /// is built). Toggled to <c>!underConstruction</c> in ApplyConstructionVisuals.
    /// </summary>
    private readonly System.Collections.Generic.List<Collider> _extraOriginalCollidersToToggle =
        new System.Collections.Generic.List<Collider>();

    /// <summary>
    /// 0..1 progress towards completion. Server-write, everyone-read. Updated by
    /// ConstructionSiteScanner (observational, between deliveries) and by
    /// CharacterAction_FinishConstruction.OnTick (authoritative, during the action).
    /// Reset to 0 at construction start; frozen at 1 after Complete.
    /// </summary>
    public NetworkVariable<float> ConstructionProgress = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Per-requirement delivered counts, replicated to clients so UIs can show per-type
    /// breakdown without server-side _contributedMaterials access. Indexed by position in
    /// _constructionRequirements. Server-write only.
    /// </summary>
    public NetworkList<DeliveredMaterialEntry> DeliveredMaterials = new NetworkList<DeliveredMaterialEntry>(
        new DeliveredMaterialEntry[0],
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Default Furniture")]
    [Tooltip("Furniture spawned automatically by the server when this building first comes into existence in a fresh world.\n" +
             "Skipped on save-restore — restored buildings reuse their persisted furniture state.\n" +
             "Use this for any furniture whose prefab carries a NetworkObject; nesting a network-bearing furniture\n" +
             "PrefabInstance directly inside the building prefab half-spawns the child and NRE's NGO sync.\n" +
             "As of 2026-05-01: Furniture authored as nested children of the building prefab is auto-captured into this list at runtime by ConvertNestedNetworkFurnitureToLayout(); manual authoring of slots is still supported. If both a manual slot AND a nested child target the same ItemSO, the nested child WINS (its pose replaces the manual slot's) and a log is emitted — remove the manual slot to silence it.")]
    [UnityEngine.Serialization.FormerlySerializedAs("_defaultFurnitureLayout")]
    [SerializeField] private List<DefaultFurnitureSlot> _defaultFurnitureLayout = new List<DefaultFurnitureSlot>();

    /// <summary>
    /// Set true after <see cref="TrySpawnDefaultFurniture"/> runs so multiple OnNetworkSpawn
    /// invocations (rare, e.g. domain reload during a session) cannot duplicate the layout.
    /// Not networked — clients never spawn furniture; this flag is server-only state.
    /// </summary>
    private bool _defaultFurnitureSpawned;

    /// <summary>
    /// Set true at the end of <see cref="Start"/>, after the
    /// <c>_currentState.OnValueChanged += HandleStateChanged</c> subscription is wired.
    /// Gates <see cref="BuildInstantly"/>'s deferral coroutine: if false, BuildInstantly
    /// must wait one or more frames until Start has run, otherwise the state-flip writes
    /// have no subscriber to drive the post-completion cascade. Per-peer state, not networked.
    /// </summary>
    private bool _isStarted;

    [Header("Identity (Dynamic)")]
    [SerializeField] protected string _prefabId; // Registry lookup ID (e.g. "Shop_Armor_A")
    
    public NetworkVariable<FixedString64Bytes> NetworkBuildingId = new NetworkVariable<FixedString64Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// The character ID of whoever originally placed this building.
    /// Distinct from CommercialBuilding.Owner (who runs the business).
    /// </summary>
    public NetworkVariable<FixedString64Bytes> PlacedByCharacterId = new NetworkVariable<FixedString64Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public string BuildingName => buildingName;
    public virtual BuildingType BuildingType => _buildingType;
    public bool IsPublicLocation => _isPublicLocation;
    public Collider BuildingZone => _buildingZone;
    public Zone DeliveryZone => _deliveryZone;
    public string PrefabId { get => _prefabId; set => _prefabId = value; }
    public string BuildingId => NetworkBuildingId.Value.ToString();

    /// <summary>
    /// Human-readable label for this building. Used by WorkPlaceRecord (snapshot at first
    /// punch-in) and any UI / log surface that wants to show "where" a worker punched in.
    ///
    /// Defaults to the authored <see cref="BuildingName"/> Inspector field; if that's blank
    /// (legacy prefabs / dynamically-named instances), falls back to the GameObject name.
    /// Subclasses may override to compose a friendlier label (e.g. owner + business name).
    ///
    /// Stable identity for keying still belongs to <see cref="BuildingId"/> — this is purely
    /// the display string captured ONCE in <c>WorkPlaceRecord.BuildingDisplayName</c> at
    /// first work-time, so renaming later doesn't retroactively rewrite history.
    /// </summary>
    public virtual string BuildingDisplayName =>
        string.IsNullOrEmpty(buildingName) ? name : buildingName;

    /// <summary>
    /// True if a spawned interior MapController exists for this building.
    /// Distinct from <see cref="SupportsInterior"/> — interiors lazy-spawn on first
    /// entry through a <c>BuildingInteriorDoor</c>, so a building authored with an
    /// interior prefab still reports <c>HasInterior == false</c> until someone walks in.
    /// </summary>
    public bool HasInterior
    {
        get
        {
            if (BuildingInteriorRegistry.Instance == null) return false;
            return BuildingInteriorRegistry.Instance.TryGetInterior(BuildingId, out _);
        }
    }

    /// <summary>
    /// True if this building's <see cref="PrefabId"/> has a non-null InteriorPrefab
    /// registered in <c>WorldSettingsData.BuildingRegistry</c> — i.e. the building was
    /// *designed* to have an interior, regardless of whether one is currently spawned.
    /// Returns false when <see cref="PrefabId"/> is empty (legacy / scene-static-only
    /// buildings) or when no <see cref="WorldSettingsData"/> is reachable. Pair with
    /// <see cref="HasInterior"/> to disambiguate "authored to have one" vs "spawned".
    /// </summary>
    public bool SupportsInterior
    {
        get
        {
            if (string.IsNullOrEmpty(_prefabId)) return false;
            var settings = GetCachedWorldSettings();
            if (settings == null) return false;
            return settings.GetInteriorPrefab(_prefabId) != null;
        }
    }

    // Cached one-shot lookup of WorldSettingsData. Resources.Load is internally cached but
    // a static field skips the call entirely on hot paths (debug overlays / per-frame
    // inspector refresh). Falsy load attempts are remembered so we don't re-probe every
    // frame when the asset is genuinely missing.
    private static WorldSettingsData _cachedWorldSettings;
    private static bool _worldSettingsLoadAttempted;

    private static WorldSettingsData GetCachedWorldSettings()
    {
        if (_cachedWorldSettings != null) return _cachedWorldSettings;
        if (_worldSettingsLoadAttempted) return null;
        _worldSettingsLoadAttempted = true;
        _cachedWorldSettings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
        return _cachedWorldSettings;
    }

    /// <summary>
    /// Returns the interior MapController for this building, or null if none spawned yet.
    /// </summary>
    public MapController GetInteriorMap()
    {
        if (BuildingInteriorRegistry.Instance == null) return null;
        if (!BuildingInteriorRegistry.Instance.TryGetInterior(BuildingId, out var record)) return null;
        return MapController.GetByMapId(record.InteriorMapId);
    }

    /// <summary>
    /// Returns the interior map ID for this building, or null if no interior registered.
    /// </summary>
    public string GetInteriorMapId()
    {
        if (BuildingInteriorRegistry.Instance == null) return null;
        if (!BuildingInteriorRegistry.Instance.TryGetInterior(BuildingId, out var record)) return null;
        return record.InteriorMapId;
    }

    private NetworkVariable<MWI.WorldSystem.BuildingState> _currentState = new NetworkVariable<MWI.WorldSystem.BuildingState>(
        MWI.WorldSystem.BuildingState.Complete,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public MWI.WorldSystem.BuildingState CurrentState => _currentState.Value;
    public bool IsUnderConstruction => _currentState.Value == MWI.WorldSystem.BuildingState.UnderConstruction;
    public System.Action OnConstructionComplete;

    /// <summary>
    /// Read-only view of the authored material requirements for construction. Empty when the
    /// building was authored without a build phase.
    /// </summary>
    public IReadOnlyList<CraftingIngredient> ConstructionRequirements => _constructionRequirements;

    /// <summary>
    /// Read-only view of the materials contributed so far. Server-authoritative; clients see the
    /// last replicated snapshot via <see cref="GetPendingMaterials"/>'s callers — note this dict
    /// itself is not networked, so client peers will see an empty contributed set. Inspector
    /// surfaces should fall back to <see cref="GetPendingMaterials"/> when running client-side.
    /// </summary>
    public IReadOnlyDictionary<ItemSO, int> ContributedMaterials => _contributedMaterials;

    public ComplexRoom MainRoom => this;

    // Use inherited GetAllRooms() to replace the old _rooms list logic
    public IEnumerable<Room> Rooms => GetAllRooms();

    protected override void Awake()
    {
        base.Awake(); // Will initialize Room and Zone logic
        
        // Auto-populate SubRooms if the user forgot to assign them in the inspector
        if (_subRooms.Count == 0)
        {
            Room[] childRooms = GetComponentsInChildren<Room>();
            foreach (Room r in childRooms)
            {
                if (r != this) AddSubRoom(r);
            }
        }

        // Strip nested-NetworkObject Furniture children → _defaultFurnitureLayout entries.
        // Runs on every peer (server + clients); each peer destroys its own copy of the
        // children so NGO never half-spawns them. See spec
        // docs/superpowers/specs/2026-05-01-building-default-furniture-auto-conversion-design.md
        ConvertNestedNetworkFurnitureToLayout();

        // NOTE: Construction state init MUST live in OnNetworkSpawn, not here. NGO does
        // not set IsServer reliably during Awake (it's gated on NetworkObject.IsSpawned),
        // so the original IsServer check in Awake never fired and _currentState stayed at
        // its field default (Complete). Moved to OnNetworkSpawn (2026-05-06).
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Initialize construction state based on requirements. MUST live here, not in Awake —
        // NGO does not set IsServer reliably until after the NetworkObject spawns. On server,
        // this writes through the NetworkVariable and replicates to clients. (2026-05-06.)
        // _spawnAsComplete overrides the auto-derivation for scene-authored buildings that
        // should skip the construction loop. RestoreFromSaveData runs AFTER OnNetworkSpawn
        // and overrides this for saved buildings.
        if (IsServer)
        {
            int reqCount = _constructionRequirements?.Count ?? 0;
            MWI.WorldSystem.BuildingState newState;
            if (_spawnAsComplete)
            {
                newState = MWI.WorldSystem.BuildingState.Complete;
            }
            else
            {
                newState = (reqCount > 0)
                    ? MWI.WorldSystem.BuildingState.UnderConstruction
                    : MWI.WorldSystem.BuildingState.Complete;
            }
            if (_currentState.Value != newState) _currentState.Value = newState;
            Debug.Log($"<color=cyan>[Building.OnNetworkSpawn]</color> {buildingName} reqs={reqCount} spawnAsComplete={_spawnAsComplete} → state={_currentState.Value}");
        }

        if (IsServer && NetworkBuildingId.Value.IsEmpty)
        {
            // Scene-authored buildings (no PlacedByCharacterId) need a STABLE id derived from
            // their world position so the same building keeps the same BuildingId across
            // reloads. Otherwise BuildingInteriorRegistry.RestoreState restores interior
            // records keyed by an obsolete GUID — re-entering the door spawns a fresh interior
            // instead of the saved one.
            //
            // Runtime-placed buildings (BuildingPlacementManager sets PlacedByCharacterId
            // before Spawn) round-trip their GUID through BuildingSaveData on save/load, so
            // they keep using a fresh Guid.NewGuid() at first spawn.
            if (PlacedByCharacterId.Value.IsEmpty)
            {
                NetworkBuildingId.Value = DeriveDeterministicSceneBuildingId(gameObject.scene.name, transform.position);
                Debug.Log($"<color=green>[Building]</color> Derived deterministic ID for scene-authored '{buildingName}' at {transform.position}: {BuildingId}");
            }
            else
            {
                NetworkBuildingId.Value = Guid.NewGuid().ToString("N");
                Debug.Log($"<color=green>[Building]</color> Generated unique ID for runtime-placed {buildingName}: {BuildingId}");
            }
        }

        // NavMesh carving must NOT happen while UnderConstruction — otherwise the navmesh
        // bakes the completed-building geometry as an obstacle even though the visuals/
        // colliders are disabled by ApplyConstructionVisuals. Players couldn't path through
        // a half-built building's footprint. Only carve once state hits Complete (initial
        // spawn for instant-build prefabs, or HandleStateChanged for the construction-loop
        // path).
        if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete)
        {
            ConfigureNavMeshObstacles();
        }

        // Server-only: spawn default furniture *only* after Complete. Pre-Complete spawns
        // would put usable furniture inside an unfinished building (visually inside the
        // scaffolding) and create operational gameplay before the construction loop
        // finishes. The state-change handler kicks the spawn when state flips to Complete.
        if (IsServer && _currentState.Value == MWI.WorldSystem.BuildingState.Complete)
        {
            TrySpawnDefaultFurniture();
        }
    }

    /// <summary>
    /// Hashes scene name + world position (rounded to mm) into a stable 32-char hex GUID.
    /// Two scene buildings cannot occupy the exact same authored position, so the seed is
    /// unique per building and survives reloads (no Guid.NewGuid() drift).
    /// </summary>
    private static string DeriveDeterministicSceneBuildingId(string sceneName, Vector3 worldPos)
    {
        long x = (long)Mathf.RoundToInt(worldPos.x * 1000f);
        long y = (long)Mathf.RoundToInt(worldPos.y * 1000f);
        long z = (long)Mathf.RoundToInt(worldPos.z * 1000f);
        string seed = $"{sceneName}|{x}|{y}|{z}";

        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
            return new Guid(hash).ToString("N");
        }
    }

    /// <summary>
    /// Triggers a rebuild of the single scene-root <c>NavmeshSurface</c> so the
    /// newly-placed building's actual 3D geometry (MeshColliders, concave shell
    /// walls, roof cutouts, etc.) is re-included in the world navmesh. This
    /// preserves the pre-Phase1 behavior of PRECISE carving matching the
    /// building's real mesh — the previous <c>BuildNavMesh()</c> approach — while
    /// avoiding the old multi-layer bug by always targeting ONE surface instead
    /// of spawning a per-MapController surface on every placement.
    ///
    /// NavMeshObstacle(carve=true) components on building prefabs still work
    /// alongside this — they provide cheap runtime carving if the prefab is
    /// moved/re-parented, while the bake gives precise initial shape.
    ///
    /// Runs on every peer (server + clients) because NavMesh data is runtime-only
    /// and not replicated — each peer needs its own up-to-date navmesh.
    /// </summary>
    private void ConfigureNavMeshObstacles()
    {
        RebuildWorldNavMesh();
    }

    private static void RebuildWorldNavMesh()
    {
        NavMeshSurface worldSurface = FindWorldNavMeshSurface();
        if (worldSurface == null)
        {
            Debug.LogWarning("<color=orange>[Building]</color> Cannot rebuild world navmesh: no scene-root 'NavmeshSurface' with a NavMeshSurface component found.");
            return;
        }

        worldSurface.BuildNavMesh();
        Debug.Log($"<color=cyan>[Building]</color> World navmesh rebuilt to include new building geometry.");
    }

    private static NavMeshSurface _cachedWorldSurface;

    private static NavMeshSurface FindWorldNavMeshSurface()
    {
        if (_cachedWorldSurface != null) return _cachedWorldSurface;

        // Scene convention: the world-wide surface lives on a root GameObject
        // literally named "NavmeshSurface". Search by name first (cheap); fall
        // back to the first matching surface with collectObjects=All.
        GameObject byName = GameObject.Find("NavmeshSurface");
        if (byName != null)
        {
            _cachedWorldSurface = byName.GetComponent<NavMeshSurface>();
            if (_cachedWorldSurface != null) return _cachedWorldSurface;
        }

        foreach (var surf in UnityEngine.Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
        {
            if (surf == null) continue;
            if (surf.collectObjects == CollectObjects.All)
            {
                _cachedWorldSurface = surf;
                return _cachedWorldSurface;
            }
        }
        return null;
    }

    protected virtual void Start()
    {
        // Subscribe to state changes on all clients
        _currentState.OnValueChanged += HandleStateChanged;

        // Register in Start to ensure BuildingManager.Instance is initialized
        if (BuildingManager.Instance != null)
        {
            BuildingManager.Instance.RegisterBuilding(this);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[Building]</color> {buildingName} n'a pas pu s'enregistrer car BuildingManager n'est pas dans la scène.");
        }

        // Re-scan FurnitureManager.Furnitures after every sibling's Awake/Start has finished.
        // Room.Start runs LoadExistingFurniture for the same race (nested-prefab Furniture
        // children sometimes spawn after the parent's Awake), but Room.Start is `private` and
        // is HIDDEN by Building.Start (Unity calls only the most-derived Start) — so without
        // this explicit call the Building's own MainRoom rescan never happens. Critical for
        // _defaultFurnitureLayout: the spawned crates are parented under the building root
        // (NGO requires a NetworkObject ancestor), and SpawnDefaultFurnitureSlot now defaults
        // their FurnitureManager registration to MainRoom — but if any earlier authoring
        // path skipped that, this rescan still catches them. LoadExistingFurniture is
        // additive + idempotent so re-calling is safe.
        if (FurnitureManager != null)
        {
            FurnitureManager.LoadExistingFurniture();
        }

        // Build a ghost-clone of CompletedVisual into ConstructionVisual so the under-construction
        // visual looks like a translucent silhouette of the finished building rather than a generic
        // placeholder. Runs on every peer (visuals are per-peer; nothing replicates).
        EnsureConstructionGhostVisual();

        // Apply initial visual state — late-joiners need this; HandleStateChanged only fires
        // on subsequent changes, not on the initial state replicated via NetworkVariable spawn payload.
        Debug.Log($"<color=cyan>[Building.Start]</color> {buildingName} _currentState.Value={_currentState.Value} → calling ApplyConstructionVisuals. IsServer={IsServer}");
        ApplyConstructionVisuals(_currentState.Value);

        // Marker for BuildInstantly's deferral coroutine: subscription is wired, future state
        // writes will dispatch to HandleStateChanged. Set LAST so anything queued for "after
        // Start" sees a fully initialised Building.
        _isStarted = true;
    }

    /// <summary>
    /// Clones the children of <see cref="_completedVisualRoot"/> into <see cref="_constructionVisualRoot"/>,
    /// strips colliders + network components, and (optionally) repaints all renderers with
    /// <see cref="_constructionGhostMaterial"/>. Idempotent per-instance via <see cref="_ghostVisualPopulated"/>.
    ///
    /// Runs on every peer. Visuals are local — no replication. The ghost material is optional;
    /// if null the clone uses the original materials (the scaffold visual will look like the
    /// finished building, not translucent).
    ///
    /// Wipes any existing children of ConstructionVisual first (e.g. a Scaffold_Placeholder
    /// authored at prefab time gets removed at runtime in favor of this auto-clone).
    /// </summary>
    private void EnsureConstructionGhostVisual()
    {
        if (_ghostVisualPopulated) return;
        if (_constructionVisualRoot == null) return;

        // Wipe any existing ConstructionVisual children (legacy Scaffold_Placeholder,
        // older ghost/outline clones from previous iterations of this design).
        for (int i = _constructionVisualRoot.transform.childCount - 1; i >= 0; i--)
        {
            var existing = _constructionVisualRoot.transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(existing); else DestroyImmediate(existing);
        }

        // Cache renderers on top-level Building children OUTSIDE CompletedVisual /
        // ConstructionVisual / Zones so ApplyConstructionVisuals can hide them while
        // UnderConstruction (e.g., Interior Door's wall + archway). The original
        // GameObjects keep their scripts running — only Renderer.enabled is toggled.
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child == null) continue;
            var childGO = child.gameObject;
            if (childGO == _constructionVisualRoot) continue;
            if (childGO == _completedVisualRoot) continue;
            if (childGO.GetComponent<Zone>() != null) continue;

            var rs = childGO.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (rs == null || rs.Length == 0) continue;
            foreach (var r in rs) if (r != null) _extraOriginalRenderersToToggle.Add(r);

            // Colliders too — so the player can walk through the footprint AND can't
            // interact with door E-prompts before the building is built. We disable
            // BOTH solid colliders (movement blockers) AND trigger colliders
            // (InteractionZone for E-prompt) on these extra children, since both should
            // be inactive during construction.
            var cs = childGO.GetComponentsInChildren<Collider>(includeInactive: true);
            if (cs != null)
            {
                foreach (var c in cs)
                {
                    if (c == null) continue;
                    _extraOriginalCollidersToToggle.Add(c);
                }
            }
        }

        // Drop-zone footprint marker — flat ground rectangle.
        EnsureConstructionFootprintMarker();

        // Vertical "curtain" barrier particles rising from the footprint perimeter,
        // fading alpha bottom → top over lifetime. Auto-toggles with ConstructionVisual.
        EnsureConstructionCurtainParticles();

        // Static translucent wall around the perimeter, alpha fades ground → top via
        // vertex color. Reads WallMaterial / WallHeight / WallAlpha* from the same
        // BuildingCurtainSettings asset as the particles. Auto-toggles with ConstructionVisual.
        EnsureConstructionPerimeterWall();

        _ghostVisualPopulated = true;
    }

    /// <summary>
    /// Spawns a single ParticleSystem child of <see cref="_constructionVisualRoot"/> that
    /// emits upward-rising particles from the BuildingZone footprint perimeter (BoxEdge
    /// shape, Y collapsed to 0). Color-over-lifetime fades alpha bottom → top so the
    /// effect reads as a translucent barrier wall.
    ///
    /// No-op if BuildingZone is not a BoxCollider or _constructionCurtainMaterial is null.
    /// </summary>
    private void EnsureConstructionCurtainParticles()
    {
        // ── Pre-spawn validation — fail fast WITHOUT creating any GameObject so the
        // Console clearly shows which dependency is missing before we touch the scene.
        if (_constructionVisualRoot == null) { Debug.LogWarning($"[Building.Curtain] {buildingName}: _constructionVisualRoot null — skip"); return; }
        if (_constructionCurtainMaterial == null) { Debug.LogWarning($"[Building.Curtain] {buildingName}: _constructionCurtainMaterial null — skip"); return; }
        if (!(_buildingZone is BoxCollider box)) { Debug.LogWarning($"[Building.Curtain] {buildingName}: _buildingZone not a BoxCollider — skip"); return; }

        // ── Resolve settings via the BuildingCurtainSettingsHolder component on this
        // Building's root. The holder is authored once on Building_prefab.prefab and every
        // building variant inherits it via Unity's prefab inheritance. Per-variant override
        // is the standard prefab-override workflow on the holder's _settings field. If the
        // component is missing or its asset is null, skip the curtain (with a warning) —
        // preferred to hidden defaults so the missing config is visible.
        var holder = GetComponent<BuildingCurtainSettingsHolder>();
        if (holder == null)
        {
            Debug.LogWarning($"[Building.Curtain] {buildingName}: BuildingCurtainSettingsHolder component missing on this Building's root GameObject — skip. " +
                             "Add the component to Building_prefab.prefab so every building variant inherits it.");
            return;
        }
        var settings = holder.Settings;
        if (settings == null)
        {
            Debug.LogWarning($"[Building.Curtain] {buildingName}: BuildingCurtainSettingsHolder is present but its Settings field is null — skip. " +
                             "Drag a BuildingCurtainSettings asset onto the holder on Building_prefab.prefab.");
            return;
        }
        Debug.Log($"[Building.Curtain] {buildingName}: spawning curtain — settings='{settings.name}' " +
                  $"emission={settings.EmissionRate} max={settings.MaxParticles} size={settings.StartSize} " +
                  $"speed=[{settings.RiseSpeedMin},{settings.RiseSpeedMax}] alphaMax={settings.AlphaMax}");

        GameObject psHost;
        try
        {
            psHost = new GameObject("ConstructionCurtainParticles");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            return;
        }
        psHost.transform.SetParent(_constructionVisualRoot.transform, worldPositionStays: false);

        // Position at the BOTTOM face of BuildingZone, lifted 0.02 to avoid Z-fight.
        float bottomY = box.center.y - (box.size.y * 0.5f) + 0.02f;
        psHost.transform.localPosition = new Vector3(box.center.x, bottomY, box.center.z);
        psHost.transform.localRotation = Quaternion.identity;

        var ps = psHost.AddComponent<ParticleSystem>();

        // Stop the system before configuring (Unity quirk: must call Stop before changing
        // shape/main on a fresh component, otherwise some sub-modules silently ignore writes).
        ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmittingAndClear);

        // ── Main module. CRITICAL: startSpeed = 0 — BoxEdge shape's startSpeed direction is
        // RADIAL OUTWARD in the X-Z plane (along the floor). We zero it and rely entirely on
        // velocityOverLifetime.y to drive vertical motion.
        // Tunable knobs live on the BuildingCurtainSettings asset (Resources/BuildingCurtainSettings.asset).
        // Column height ≈ RiseSpeed × StartLifetime (project scale: 11 u = 1.67 m).
        float lifetime = Mathf.Max(0.01f, settings.StartLifetime);
        float startSize = Mathf.Max(0f, settings.StartSize);
        Color tintRGBA = new Color(settings.Tint.r, settings.Tint.g, settings.Tint.b, 1f);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = startSize;
        main.startColor = tintRGBA;
        main.maxParticles = Mathf.Max(0, settings.MaxParticles);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;

        // ── Emission: tunable rate. Lower → fewer particles → thinner wall.
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = Mathf.Max(0f, settings.EmissionRate);

        // ── Shape: BoxEdge, X×Z = footprint, Y collapsed → particles spawn around the
        // perimeter rectangle on the floor.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.BoxEdge;
        shape.scale = new Vector3(box.size.x, 0.001f, box.size.z);

        // ── Velocity over lifetime: visible upward drift driven by the tunable rise-speed
        // range. All three axes MUST use the same MinMaxCurve mode (Unity warning otherwise) —
        // we use TwoConstants on all three; X/Z are constant 0 → 0 (no horizontal drift).
        float vMin = Mathf.Max(0f, settings.RiseSpeedMin);
        float vMax = Mathf.Max(vMin, settings.RiseSpeedMax);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(vMin, vMax);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // ── Color over lifetime: alpha 0 → AlphaMax (fade-in) → AlphaMax (plateau) → 0
        // (fade-out). Tunable via settings.AlphaMax / FadeInEnd / FadeOutStart.
        // Strict ordering 0 < t1 < t2 < 1 enforced so Unity's gradient sampling stays defined.
        float alphaMax = Mathf.Clamp01(settings.AlphaMax);
        float t1 = Mathf.Clamp(settings.FadeInEnd, 0.001f, 0.998f);
        float t2 = Mathf.Clamp(settings.FadeOutStart, t1 + 0.001f, 0.999f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(tintRGBA, 0f),
                new GradientColorKey(tintRGBA, 1f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(alphaMax, t1),
                new GradientAlphaKey(alphaMax, t2),
                new GradientAlphaKey(0f, 1f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // ── Renderer: standard view-aligned billboard. We tried Stretch mode but URP/Particles/Unlit
        // doesn't always honour the velocity stretching keywords, leaving particles invisible.
        // Billboard guarantees visibility; the curtain feel comes from large startSize +
        // dense emission overlapping vertically.
        var psr = psHost.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            psr.material = _constructionCurtainMaterial;
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment = ParticleSystemRenderSpace.View;
        }

        ps.Play(withChildren: true);
    }

    /// <summary>
    /// Spawns a procedurally-built 4-quad sleeve mesh around the BuildingZone perimeter
    /// to delimit the construction zone with a translucent wall whose alpha fades from
    /// opaque at the ground to transparent at the top. The fade is driven by per-vertex
    /// color alpha (no custom shader needed — URP/Particles/Unlit's _ColorMode=Multiply
    /// applies vertex color to the final pixel).
    ///
    /// No-op if the BuildingCurtainSettingsHolder is missing, its Settings is null, the
    /// settings asset has no WallMaterial assigned, WallHeight ≤ 0, or BuildingZone is
    /// not a BoxCollider.
    /// </summary>
    private void EnsureConstructionPerimeterWall()
    {
        if (_constructionVisualRoot == null) return; // already warned by curtain method
        if (!(_buildingZone is BoxCollider box)) return;

        var holder = GetComponent<BuildingCurtainSettingsHolder>();
        if (holder == null || holder.Settings == null) return; // already warned by curtain method
        var settings = holder.Settings;

        if (settings.WallMaterial == null)
        {
            // Wall is optional — silent skip if no material is wired up.
            return;
        }
        if (settings.WallHeight <= 0f) return;

        GameObject host;
        try
        {
            host = new GameObject("ConstructionPerimeterWall");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            return;
        }
        host.transform.SetParent(_constructionVisualRoot.transform, worldPositionStays: false);

        // Origin sits at the BOTTOM face of BuildingZone (lifted 0.02 to avoid Z-fight
        // with the footprint marker). Mesh verts are then in local Y from 0 to WallHeight.
        float bottomY = box.center.y - (box.size.y * 0.5f) + 0.02f;
        host.transform.localPosition = new Vector3(box.center.x, bottomY, box.center.z);
        host.transform.localRotation = Quaternion.identity;

        // ── Build the 4-quad sleeve mesh. Single mesh = single draw call.
        // Vertex layout: 4 verts per side × 4 sides = 16 verts.
        // Per side, vertex order is BL, BR, TR, TL with bottom verts at y=0 (alpha=Bottom)
        // and top verts at y=h (alpha=Top). Two triangles per side: BL-TR-BR and BL-TL-TR.
        float hx = box.size.x * 0.5f;
        float hz = box.size.z * 0.5f;
        float h = settings.WallHeight;

        Color cBot = new Color(settings.WallColor.r, settings.WallColor.g, settings.WallColor.b, Mathf.Clamp01(settings.WallAlphaBottom));
        Color cTop = new Color(settings.WallColor.r, settings.WallColor.g, settings.WallColor.b, Mathf.Clamp01(settings.WallAlphaTop));

        var verts = new Vector3[16];
        var cols = new Color[16];
        var uvs = new Vector2[16];
        var idx = new int[24];
        int v = 0;
        int t = 0;

        void AddSide(Vector3 bl, Vector3 br)
        {
            // bl/br are BOTTOM-LEFT / BOTTOM-RIGHT corners. tr/tl are derived by raising y.
            verts[v + 0] = bl;                                 cols[v + 0] = cBot; uvs[v + 0] = new Vector2(0f, 0f);
            verts[v + 1] = br;                                 cols[v + 1] = cBot; uvs[v + 1] = new Vector2(1f, 0f);
            verts[v + 2] = new Vector3(br.x, h, br.z);         cols[v + 2] = cTop; uvs[v + 2] = new Vector2(1f, 1f);
            verts[v + 3] = new Vector3(bl.x, h, bl.z);         cols[v + 3] = cTop; uvs[v + 3] = new Vector2(0f, 1f);
            idx[t + 0] = v + 0; idx[t + 1] = v + 2; idx[t + 2] = v + 1;
            idx[t + 3] = v + 0; idx[t + 4] = v + 3; idx[t + 5] = v + 2;
            v += 4;
            t += 6;
        }

        // Walk the perimeter clockwise viewed from above. The four sides:
        AddSide(new Vector3(-hx, 0f, -hz), new Vector3( hx, 0f, -hz)); // South (-Z face)
        AddSide(new Vector3( hx, 0f, -hz), new Vector3( hx, 0f,  hz)); // East  (+X face)
        AddSide(new Vector3( hx, 0f,  hz), new Vector3(-hx, 0f,  hz)); // North (+Z face)
        AddSide(new Vector3(-hx, 0f,  hz), new Vector3(-hx, 0f, -hz)); // West  (-X face)

        var mesh = new Mesh
        {
            name = $"ConstructionPerimeterWall_Mesh ({buildingName})",
            hideFlags = HideFlags.DontSave,
        };
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.uv = uvs;
        mesh.triangles = idx;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mf = host.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = host.AddComponent<MeshRenderer>();
        mr.sharedMaterial = settings.WallMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        Debug.Log($"[Building.Wall] {buildingName}: built perimeter wall — size={box.size.x:F2}×{box.size.z:F2}, " +
                  $"height={h:F2}, alpha=[{settings.WallAlphaBottom:F2}→{settings.WallAlphaTop:F2}]");
    }

    /// <summary>
    /// Spawns a flat translucent quad child of <see cref="_constructionVisualRoot"/> sized
    /// to <see cref="_buildingZone"/>'s BoxCollider. Players see this rectangle on the
    /// ground while UnderConstruction — it marks the drop zone for construction items.
    /// No-op if BuildingZone isn't a BoxCollider or _constructionFootprintMaterial isn't set.
    /// </summary>
    private void EnsureConstructionFootprintMarker()
    {
        if (_constructionVisualRoot == null) { Debug.LogWarning($"[Building.Footprint] {buildingName}: _constructionVisualRoot null — skip"); return; }
        if (_constructionFootprintMaterial == null) { Debug.LogWarning($"[Building.Footprint] {buildingName}: _constructionFootprintMaterial null — skip"); return; }
        if (!(_buildingZone is BoxCollider box)) { Debug.LogWarning($"[Building.Footprint] {buildingName}: _buildingZone not a BoxCollider — skip"); return; }
        Debug.Log($"[Building.Footprint] {buildingName}: creating marker — bzCenter={box.center} bzSize={box.size}");

        GameObject marker;
        try
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            return;
        }
        marker.name = "ConstructionFootprintMarker";

        // Strip the Collider Unity adds to primitives — purely visual.
        var col = marker.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }

        marker.transform.SetParent(_constructionVisualRoot.transform, worldPositionStays: false);

        // Place at the BOTTOM face of BuildingZone, lifted 0.02 to avoid Z-fight with ground.
        // box values are in local space relative to the box's transform, which here is
        // the same hierarchy as ConstructionVisual (both children of building root), so the
        // bottom face Y in building-local space is box.center.y - size.y/2.
        float bottomY = box.center.y - (box.size.y * 0.5f) + 0.02f;
        marker.transform.localPosition = new Vector3(box.center.x, bottomY, box.center.z);
        marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lay flat (Quad faces +Z by default)
        marker.transform.localScale = new Vector3(box.size.x, box.size.z, 1f);

        var renderer = marker.GetComponent<Renderer>();
        if (renderer != null) renderer.material = _constructionFootprintMaterial;
    }

    private void HandleStateChanged(MWI.WorldSystem.BuildingState previousValue, MWI.WorldSystem.BuildingState newValue)
    {
        Debug.Log($"<color=cyan>[Building.HandleStateChanged]</color> {buildingName} {previousValue} → {newValue} | IsServer={IsServer}");

        // Visual swap runs on every peer (client + server), every state change.
        ApplyConstructionVisuals(newValue);

        if (newValue == MWI.WorldSystem.BuildingState.Complete)
        {
            OnConstructionComplete?.Invoke();

            // NavMesh carving fires on EVERY peer when the building completes — peers all
            // need to see the new obstacle. Skipped while UnderConstruction so the player
            // can walk freely through the half-built footprint (see OnNetworkSpawn note).
            try { ConfigureNavMeshObstacles(); }
            catch (System.Exception e) { Debug.LogException(e, this); }

            // Server-only post-completion side effects.
            if (IsServer)
            {
                // Default-furniture spawn was deferred from OnNetworkSpawn until completion —
                // run it now (idempotent via _defaultFurnitureSpawned guard).
                try { TrySpawnDefaultFurniture(); }
                catch (System.Exception e) { Debug.LogException(e, this); }

                // Eject any leftover items still on the footprint (over-delivered or wrong-type).
                try { EvictLeftoversToPerimeter(); }
                catch (System.Exception e) { Debug.LogException(e, this); }
            }

            // Sync state to CommunityData so hibernation save data stays accurate
            if (IsServer && MWI.WorldSystem.MapRegistry.Instance != null)
            {
                foreach (var comm in MWI.WorldSystem.MapRegistry.Instance.GetAllCommunities())
                {
                    var entry = comm.ConstructedBuildings.Find(b => b.BuildingId == BuildingId);
                    if (entry != null)
                    {
                        entry.State = MWI.WorldSystem.BuildingState.Complete;
                        entry.ConstructionProgress = 1f;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Toggles _constructionVisualRoot vs _completedVisualRoot based on the current state.
    /// Idempotent — safe to call repeatedly. Each peer runs this locally on every
    /// _currentState.OnValueChanged (registered in Start).
    /// </summary>
    private void ApplyConstructionVisuals(MWI.WorldSystem.BuildingState state)
    {
        bool underConstruction = (state == MWI.WorldSystem.BuildingState.UnderConstruction);

        bool conActive = _constructionVisualRoot != null ? _constructionVisualRoot.activeSelf : false;
        bool cmpActive = _completedVisualRoot != null ? _completedVisualRoot.activeSelf : false;

        if (_constructionVisualRoot != null && _constructionVisualRoot.activeSelf != underConstruction)
            _constructionVisualRoot.SetActive(underConstruction);

        if (_completedVisualRoot != null && _completedVisualRoot.activeSelf == underConstruction)
            _completedVisualRoot.SetActive(!underConstruction);

        // Hide originals of any extra-tracked children (e.g., InteriorDoor's wall) while
        // UnderConstruction so the opaque originals don't render over our footprint marker
        // AND don't block character movement on the construction site.
        // We disable Renderer.enabled / Collider.enabled rather than the host GameObject so
        // scripts/NetworkObjects on the original keep running.
        bool showOriginals = !underConstruction;
        for (int i = 0; i < _extraOriginalRenderersToToggle.Count; i++)
        {
            var r = _extraOriginalRenderersToToggle[i];
            if (r == null) continue;
            if (r.enabled != showOriginals) r.enabled = showOriginals;
        }
        for (int i = 0; i < _extraOriginalCollidersToToggle.Count; i++)
        {
            var c = _extraOriginalCollidersToToggle[i];
            if (c == null) continue;
            if (c.enabled != showOriginals) c.enabled = showOriginals;
        }

        Debug.Log($"<color=cyan>[Building.ApplyVisuals]</color> {buildingName} state={state} (under={underConstruction}) | conRoot={(_constructionVisualRoot != null ? _constructionVisualRoot.name : "NULL")} was={conActive} now={(_constructionVisualRoot != null && _constructionVisualRoot.activeSelf)} | cmpRoot={(_completedVisualRoot != null ? _completedVisualRoot.name : "NULL")} was={cmpActive} now={(_completedVisualRoot != null && _completedVisualRoot.activeSelf)} | extraToggled={_extraOriginalRenderersToToggle.Count} | IsServer={IsServer} IsClient={IsClient}");
    }

    protected virtual void OnDestroy()
    {
        if (BuildingManager.Instance != null)
        {
            BuildingManager.Instance.UnregisterBuilding(this);
        }

        // Rule #16: unsubscribe from NetworkVariable callbacks to prevent leaks.
        // _currentState lives on this Building NetworkBehaviour, so the lifecycles couple,
        // but we keep the explicit -= anyway for defense in depth and to match the rule.
        _currentState.OnValueChanged -= HandleStateChanged;
    }

    /// <summary>
    /// Permet à un NPC ou au jeu de tenter d'installer un meuble n'importe où dans ce bâtiment.
    /// Il va chercher la première Room (ou SubRoom) qui a de la place sur sa grille.
    /// </summary>
    public bool AttemptInstallFurniture(Furniture furniturePrefab)
    {
        if (furniturePrefab == null) return false;

        // On récupère toutes les salles du bâtiment (la salle principale + toutes les sous-salles)
        var allRooms = Rooms;

        foreach (var room in allRooms)
        {
            // On vérifie si la pièce a un FurnitureManager valide
            if (room.FurnitureManager != null && room.FurnitureManager.Grid != null)
            {
                // Demande à la grille de trouver une position libre pour cette taille
                Vector3? freePos = room.FurnitureManager.Grid.GetRandomFreePosition(furniturePrefab.SizeInCells);
                
                if (freePos.HasValue)
                {
                    // Convertir la position grille (monde) en appel d'ajout
                    if (room.FurnitureManager.AddFurniture(furniturePrefab, freePos.Value))
                    {
                        Debug.Log($"<color=green>[Building]</color> {furniturePrefab.name} installé avec succès dans {room.RoomName} de {BuildingName}.");
                        return true; // Succès, on arrête la recherche
                    }
                }
            }
        }

        Debug.LogWarning($"<color=orange>[Building]</color> Impossible d'installer {furniturePrefab.name} dans {BuildingName} : pas de place trouvée.");
        return false;
    }

    /// <summary>
    /// Trouve un point valide à l'intérieur du Collider BuildingZone (s'il y en a un),
    /// sinon utilise le point aléatoire de la zone générale.
    /// Idéal pour que les employés se rendent dans l'espace formel du bâtiment
    /// pour pointer ou s'y promener.
    /// </summary>
    public Vector3 GetRandomPointInBuildingZone(float yCoord)
    {
        if (_buildingZone != null)
        {
            float randomX = UnityEngine.Random.Range(_buildingZone.bounds.min.x, _buildingZone.bounds.max.x);
            float randomZ = UnityEngine.Random.Range(_buildingZone.bounds.min.z, _buildingZone.bounds.max.z);
            Vector3 targetPoint = new Vector3(randomX, yCoord, randomZ);

            if (UnityEngine.AI.NavMesh.SamplePosition(targetPoint, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                return hit.position;
            }
            return targetPoint;
        }

        return base.GetRandomPointInZone();
    }

    // Reused per Rule #34 — zero per-tick allocation. Static is safe here: scanner is
    // server-only and runs sequentially across all buildings (one tick at a time, no
    // concurrent OverlapBoxNonAlloc calls).
    private static readonly Collider[] _itemOverlapBuffer = new Collider[64];

    /// <summary>
    /// Returns physical, uncarried WorldItems whose colliders overlap the BoxCollider
    /// passed in. Allocation-light: uses a reused Collider[] buffer.
    /// Server-side use: ConstructionSiteScanner passes _buildingZone, EvictLeftoversToPerimeter
    /// passes _buildingZone, GetPhysicalItemsInZone delegates here with zone.GetComponent&lt;BoxCollider&gt;().
    /// </summary>
    public List<WorldItem> GetPhysicalItemsInCollider(Collider collider, List<WorldItem> resultBuffer = null)
    {
        var items = resultBuffer ?? new List<WorldItem>();
        items.Clear();
        if (collider == null) return items;
        if (!(collider is BoxCollider boxCol)) return items; // only BoxCollider supported

        Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
        Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;
        Quaternion rot = boxCol.transform.rotation;

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _itemOverlapBuffer, rot, Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var col = _itemOverlapBuffer[i];
            if (col == null) continue;

            var worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (worldItem != null && !worldItem.IsBeingCarried && !items.Contains(worldItem))
            {
                items.Add(worldItem);
            }
        }
        return items;
    }

    /// <summary>
    /// Retourne tous les WorldItem posés physiquement dans la zone spécifiée.
    /// Utile pour inspecter les StorageZone, DepositZone, DeliveryZone.
    /// Existing zone-shaped overload, retained for compatibility with delivery / storage
    /// zone consumers. Delegates to GetPhysicalItemsInCollider via the zone's BoxCollider.
    /// </summary>
    public List<WorldItem> GetPhysicalItemsInZone(Zone zone)
    {
        var items = new List<WorldItem>();
        if (zone == null) return items;
        var boxCol = zone.GetComponent<BoxCollider>();
        return GetPhysicalItemsInCollider(boxCol, items);
    }

    /// <summary>
    /// Forcibly builds the building instantly, bypassing all material requirements.
    ///
    /// Server-only. Unifies with the construction-loop completion path (<see cref="Finalize"/>)
    /// by writing the same NetworkVariable state flip and letting <see cref="HandleStateChanged"/>
    /// drive the entire post-completion cascade (visual swap, navmesh carve, default-furniture
    /// spawn, leftover eviction, <see cref="OnConstructionComplete"/> event). Single source of
    /// truth at the OnValueChanged subscriber.
    ///
    /// Lifecycle note: <see cref="BuildingPlacementManager"/>'s instant-mode path calls this
    /// SYNCHRONOUSLY right after <c>NetworkObject.Spawn()</c> returns — i.e. before <see cref="Start"/>
    /// has run. The <c>_currentState.OnValueChanged += HandleStateChanged</c> subscription only
    /// gets wired in <see cref="Start"/> (per Unity NetworkBehaviour conventions for this
    /// codebase), so a same-frame state flip would have no subscriber and the cascade would
    /// silently no-op. To stay on the unified `state-flip → OnValueChanged → HandleStateChanged`
    /// path, we DEFER the state flip via a coroutine that polls <see cref="_isStarted"/> until
    /// Start has run. When called after Start (e.g. debug "force complete" on an existing
    /// building, save-load restore), the deferral is skipped and we flip immediately.
    ///
    /// Idempotent: a second call after the building reaches Complete is a no-op. Multiple calls
    /// before Start spawn redundant coroutines that each early-exit on the state check.
    /// </summary>
    public virtual void BuildInstantly()
    {
        if (!IsServer) return;
        if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete) return;

        Debug.Log($"<color=red>[Building.BuildInstantly]</color> {buildingName} BYPASSING construction loop — _isStarted={_isStarted} | reqs={_constructionRequirements?.Count ?? 0} | currentState={_currentState.Value}");

        if (_isStarted)
        {
            // Subscription is already wired — flip the state and let HandleStateChanged run
            // the cascade. Same path as the construction-loop's Building.Finalize.
            DoInstantBuildStateFlip();
        }
        else
        {
            // BuildingPlacementManager called us synchronously after Spawn(), before Start.
            // Defer the state flip until Start sets _isStarted. The cascade will then run
            // through HandleStateChanged → identical to the construction-loop completion path.
            StartCoroutine(BuildInstantlyAfterStart());
        }
    }

    /// <summary>
    /// Coroutine helper for <see cref="BuildInstantly"/>. Waits frame-by-frame until
    /// <see cref="_isStarted"/> is set (end of <see cref="Start"/>), then performs the
    /// state flip so <see cref="HandleStateChanged"/> drives the cascade.
    /// </summary>
    private System.Collections.IEnumerator BuildInstantlyAfterStart()
    {
        // Defensive bound: Start should run within 1 frame of OnNetworkSpawn under normal Unity
        // lifecycle. The bound prevents a runaway coroutine if the GameObject ends up disabled
        // or destroyed mid-deferral (Unity stops coroutines on destroy automatically; the bound
        // is a paranoid backstop).
        const int maxFrames = 600;
        int frames = 0;
        while (!_isStarted && frames < maxFrames)
        {
            yield return null;
            frames++;
        }

        if (!_isStarted)
        {
            Debug.LogError($"<color=red>[Building.BuildInstantly]</color> {buildingName} deferral timed out after {maxFrames} frames — Start never ran. Cascade will not fire.", this);
            yield break;
        }

        Debug.Log($"<color=red>[Building.BuildInstantly]</color> {buildingName} deferral resolved after {frames} frame(s); flipping state now.");
        DoInstantBuildStateFlip();
    }

    /// <summary>
    /// Server-only state flip shared between the synchronous and deferred branches of
    /// <see cref="BuildInstantly"/>. After this returns, <see cref="HandleStateChanged"/>
    /// runs the post-completion cascade (visual swap → navmesh carve → furniture spawn →
    /// leftover eviction → <see cref="OnConstructionComplete"/>) on every peer.
    /// </summary>
    private void DoInstantBuildStateFlip()
    {
        if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete) return;
        _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
        if (ConstructionProgress.Value < 1f) ConstructionProgress.Value = 1f;
        _contributedMaterials.Clear();
    }

    /// <summary>
    /// Contributes a material to the construction progress.
    /// </summary>
    public virtual void ContributeMaterial(ItemSO item, int amount)
    {
        if (CurrentState == MWI.WorldSystem.BuildingState.Complete) return;
        if (item == null || amount <= 0) return;

        if (!_contributedMaterials.ContainsKey(item))
        {
            _contributedMaterials[item] = 0;
        }

        _contributedMaterials[item] += amount;
        Debug.Log($"[Building] Contributed {amount}x {item.ItemName} to {buildingName} construction.");

        CheckConstructionCompletion();
    }

    protected virtual void CheckConstructionCompletion()
    {
        if (CurrentState == MWI.WorldSystem.BuildingState.Complete) return;

        bool isFinished = true;

        foreach (var req in _constructionRequirements)
        {
            int contributed = _contributedMaterials.TryGetValue(req.Item, out int c) ? c : 0;
            if (contributed < req.Amount)
            {
                isFinished = false;
                break;
            }
        }

        if (isFinished)
        {
            _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
            Debug.Log($"<color=green>[Building]</color> {buildingName} has finished construction!");
            // OnConstructionComplete will be triggered by state change callback
        }
    }

    /// <summary>
    /// Checks what materials are still outstanding for construction.
    /// </summary>
    public virtual Dictionary<ItemSO, int> GetPendingMaterials()
    {
        var pending = new Dictionary<ItemSO, int>();
        if (CurrentState == MWI.WorldSystem.BuildingState.Complete) return pending;

        foreach (var req in _constructionRequirements)
        {
            int contributed = _contributedMaterials.TryGetValue(req.Item, out int c) ? c : 0;
            if (contributed < req.Amount)
            {
                pending[req.Item] = req.Amount - contributed;
            }
        }
        return pending;
    }

    // =========================================================================
    // DEFAULT FURNITURE SPAWN (server-only)
    // =========================================================================

    /// <summary>
    /// Subclass extension point fired at the tail of <see cref="TrySpawnDefaultFurniture"/>
    /// when the building had a non-empty <c>_defaultFurnitureLayout</c> to process. Skipped
    /// when the layout is empty (no furniture to invalidate caches for). Default no-op.
    ///
    /// Override to invalidate any subclass-owned cache that depends on the just-spawned
    /// furniture. Always chain via <c>base.OnDefaultFurnitureSpawned()</c> in overrides so
    /// parent-class invalidations still run.
    /// </summary>
    protected virtual void OnDefaultFurnitureSpawned() { }

    /// <summary>
    /// Edit-time, the level designer authors furniture as nested children of this prefab
    /// (visible, easy to position). At runtime — BEFORE NGO half-spawns the nested
    /// NetworkObjects and corrupts <c>SpawnManager.SpawnedObjectsList</c> — this method:
    ///
    ///   1. Captures each network-bearing Furniture child's <see cref="FurnitureItemSO"/>
    ///      + local pose + nearest <c>Room</c> ancestor into a runtime-only
    ///      <see cref="DefaultFurnitureSlot"/> appended to <c>_defaultFurnitureLayout</c>.
    ///   2. <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/>'s the
    ///      child GameObject so NGO never sees it as a nested NetworkObject.
    ///
    /// <b>DestroyImmediate is mandatory here</b> — async <c>Destroy()</c> queues for end-of-frame,
    /// but every Building Instantiate→Spawn callsite (<c>MapController.SpawnSavedBuildings</c>,
    /// <c>MapController.WakeUp</c>, <c>BuildingPlacementManager</c>) calls <c>NetworkObject.Spawn()</c>
    /// in the SAME frame as <c>Instantiate</c>. With async Destroy the doomed children are still
    /// physically alive in the hierarchy when Spawn runs, NGO walks them via
    /// <c>GetComponentsInChildren&lt;NetworkObject&gt;()</c>, and <see cref="TrySpawnDefaultFurniture"/>'s
    /// dedup snapshot at line 727 sees them as "already present" — so every default-furniture slot
    /// is silently skipped and the building spawns empty. DestroyImmediate forces full synchronous
    /// destruction before control returns to the caller. Safe here because the doomed children's
    /// NetworkObjects have <c>IsSpawned == false</c>, are absent from <c>SpawnedObjects</c>, and
    /// belong to a child GameObject (not the GameObject whose Awake we are inside).
    ///
    /// <see cref="TrySpawnDefaultFurniture"/> (server-only, in <see cref="OnNetworkSpawn"/>)
    /// then re-spawns each appended entry as a top-level NetworkObject parented under the
    /// building. End result: same visual layout as authored, no half-spawned NOs, clients
    /// stay in sync.
    ///
    /// Runs on every peer (server + clients) because every peer's <c>Instantiate</c> of
    /// this prefab brings the nested children along; without local destruction, clients
    /// would keep broken half-registered NetworkObject children in their scene.
    ///
    /// Furniture children WITHOUT a NetworkObject (e.g. a plain-MonoBehaviour TimeClock
    /// stripped of its NO) are LEFT IN PLACE — they're legal as nested non-network children,
    /// and <see cref="TrySpawnDefaultFurniture"/>'s per-slot ItemSO dedup already handles them.
    ///
    /// All <c>_defaultFurnitureLayout</c> mutations are in-memory only on the live instance.
    /// The <c>!Application.isPlaying</c> guard prevents any edit-mode invocation, so Unity's
    /// serialization system never sees the runtime mutation — the prefab asset stays clean.
    /// </summary>
    private void ConvertNestedNetworkFurnitureToLayout()
    {
        if (!Application.isPlaying) return;

        Furniture[] children = GetComponentsInChildren<Furniture>(includeInactive: true);
        if (children == null || children.Length == 0) return;

        int converted = 0;
        int skipped = 0;
        foreach (var furniture in children)
        {
            if (furniture == null) continue;
            if (furniture.gameObject == gameObject) continue; // defensive: not on building root

            if (furniture.GetComponent<Unity.Netcode.NetworkObject>() == null)
            {
                // Plain-MonoBehaviour furniture is legal as a nested child. Leave it.
                skipped++;
                continue;
            }

            if (furniture.FurnitureItemSO == null)
            {
                Debug.LogWarning(
                    $"<color=orange>[Building]</color> {buildingName}: nested furniture '{furniture.name}' has a NetworkObject but no FurnitureItemSO — destroying without conversion (would have half-spawned anyway).",
                    this);
                DestroyImmediate(furniture.gameObject);
                continue;
            }

            Vector3 localPos = transform.InverseTransformPoint(furniture.transform.position);
            Vector3 localEuler = (Quaternion.Inverse(transform.rotation) * furniture.transform.rotation).eulerAngles;

            // Walk parent chain for the first Room ancestor between the furniture and the building
            // root (exclusive). Building inherits ComplexRoom→Room, so the building root itself has
            // a Room component — but the spec wants TargetRoom = null when furniture is parented
            // directly under the root, so we exclude the building root from the search.
            Room targetRoom = null;
            for (Transform t = furniture.transform.parent; t != null && t != transform; t = t.parent)
            {
                var room = t.GetComponent<Room>();
                if (room != null) { targetRoom = room; break; }
            }

            var slot = new DefaultFurnitureSlot
            {
                ItemSO = furniture.FurnitureItemSO,
                LocalPosition = localPos,
                LocalEulerAngles = localEuler,
                TargetRoom = targetRoom,
            };

            // Dedup against existing serialized slots — match by (ItemSO + LocalPosition).
            // Two instances of the same FurnitureItemSO at different positions are legitimately
            // separate authored slots (e.g. two crates: one for tool storage, one for seed
            // storage). Only collapse when both ItemSO AND position match (within an epsilon),
            // which only happens when a manual _defaultFurnitureLayout entry was authored AND
            // the same nested-child sat at the same world position. Converted child wins.
            const float positionEpsilon = 0.01f;
            int existingIndex = _defaultFurnitureLayout.FindIndex(s =>
                s != null
                && s.ItemSO == slot.ItemSO
                && Vector3.Distance(s.LocalPosition, slot.LocalPosition) < positionEpsilon);
            if (existingIndex >= 0)
            {
                Debug.Log(
                    $"<color=cyan>[Building]</color> {buildingName}: nested child '{furniture.name}' overrides existing manual _defaultFurnitureLayout entry [{existingIndex}] for ItemSO '{slot.ItemSO.name}' at position {slot.LocalPosition}. Remove the manual slot to silence this log.",
                    this);
                _defaultFurnitureLayout[existingIndex] = slot;
            }
            else
            {
                _defaultFurnitureLayout.Add(slot);
            }

            DestroyImmediate(furniture.gameObject);
            converted++;
        }

        if (converted > 0 || skipped > 0)
        {
            Debug.Log(
                $"<color=cyan>[Building]</color> {buildingName}: converted {converted} nested NetworkObject furniture child(ren) to _defaultFurnitureLayout (skipped {skipped} non-network furniture).",
                this);
        }
    }

    /// <summary>
    /// Server-only. Instantiates and Spawn()s entries in <see cref="_defaultFurnitureLayout"/>
    /// that don't already have a matching Furniture child on this building. The per-slot match
    /// (by FurnitureItemSO) replaces the earlier "any Furniture child present → skip all" guard,
    /// which was too aggressive: baked NetworkObject-FREE furniture (e.g. TimeClock on the Forge)
    /// is a legitimate child and would have suppressed every slot. Survives the save-restore path
    /// the same way: persisted furniture children block their corresponding slot from re-spawning,
    /// while never-baked slots still spawn on first OnNetworkSpawn.
    /// </summary>
    private void TrySpawnDefaultFurniture()
    {
        if (_defaultFurnitureSpawned) return;
        _defaultFurnitureSpawned = true;

        if (_defaultFurnitureLayout == null || _defaultFurnitureLayout.Count == 0)
        {
            return;
        }

        // Snapshot existing Furniture children once. Per-slot match is by FurnitureItemSO ref.
        var existing = GetComponentsInChildren<Furniture>(includeInactive: true);
        var existingItemSOs = new System.Collections.Generic.HashSet<FurnitureItemSO>();
        foreach (var f in existing)
        {
            if (f != null && f.FurnitureItemSO != null) existingItemSOs.Add(f.FurnitureItemSO);
        }

        for (int i = 0; i < _defaultFurnitureLayout.Count; i++)
        {
            var slot = _defaultFurnitureLayout[i];
            if (slot == null || slot.ItemSO == null || slot.ItemSO.InstalledFurniturePrefab == null)
            {
                Debug.LogWarning($"[Building] {buildingName}: _defaultFurnitureLayout[{i}] is missing ItemSO or InstalledFurniturePrefab — slot skipped.", this);
                continue;
            }

            if (existingItemSOs.Contains(slot.ItemSO))
            {
                Debug.Log($"[Building] {buildingName}: slot[{i}] '{slot.ItemSO.name}' already present as a baked or restored child — skipping spawn.", this);
                continue;
            }

            try
            {
                SpawnDefaultFurnitureSlot(slot);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        // Subclass cache invalidation hook. Default no-op; overridden by CommercialBuilding
        // (storage furniture cache) and CraftingBuilding (+ craftable cache).
        OnDefaultFurnitureSpawned();
    }

    /// <summary>
    /// Server-only. Mirrors the player furniture-place flow at
    /// <c>CharacterActions.RequestFurniturePlaceServerRpc</c>: Instantiate at world position,
    /// <c>NetworkObject.Spawn()</c> as a top-level NO, then hand off to the target room's
    /// <see cref="FurnitureManager.RegisterSpawnedFurniture"/> which re-parents the furniture
    /// under the room and registers the cell occupancy on the room's <c>FurnitureGrid</c>.
    /// NGO's <c>AutoObjectParentSync</c> replicates the post-spawn re-parent to clients.
    /// </summary>
    private void SpawnDefaultFurnitureSlot(DefaultFurnitureSlot slot)
    {
        Furniture prefab = slot.ItemSO.InstalledFurniturePrefab;
        Vector3 worldPos = transform.TransformPoint(slot.LocalPosition);
        Quaternion worldRot = transform.rotation * Quaternion.Euler(slot.LocalEulerAngles);

        Furniture instance = Instantiate(prefab, worldPos, worldRot);

        var netObj = instance.GetComponent<NetworkObject>();
        bool spawned = false;
        if (netObj != null && !netObj.IsSpawned)
        {
            netObj.Spawn();
            spawned = true;
        }

        try
        {
            // Parent under the building root — the only NetworkObject in this hierarchy. Parenting
            // under Room_Main (a NetworkBehaviour on a non-NO) throws NGO's InvalidParentException;
            // the building root is the closest valid NO ancestor. Visually the furniture lives
            // inside the building at the correct world position; logical room membership is tracked
            // in the FurnitureManager._furnitures list (see RegisterSpawnedFurnitureUnchecked notes).
            instance.transform.SetParent(transform, worldPositionStays: true);

            // Use the UNCHECKED register path: default furniture is server-authored content (the level
            // designer placed the slot), not runtime user input — CanPlaceFurniture validation is for
            // the player-place flow. Unchecked register adds to grid occupancy + the room's furniture
            // list WITHOUT touching transform.parent (we already parented above).
            //
            // Default to the building's own MainRoom (Building inherits ComplexRoom→Room and IS its
            // MainRoom) when slot.TargetRoom is null. Authoring convention previously demanded an
            // explicit Room ancestor in the parent chain — but spawned furniture parented directly
            // under the building root would silently miss FurnitureManager registration, breaking
            // every consumer that goes through Room.GetFurnitureOfType / Building.GetFurnitureOfType
            // (which the LogisticsManager + crafting pipeline rely on). Falling back to MainRoom
            // makes registration the default; designers can still set slot.TargetRoom explicitly to
            // route a slot into a specific sub-room.
            Room registerInto = slot.TargetRoom != null ? slot.TargetRoom : (Room)MainRoom;
            if (registerInto != null && registerInto.FurnitureManager != null)
            {
                registerInto.FurnitureManager.RegisterSpawnedFurnitureUnchecked(instance, worldPos);
            }
            else
            {
                Debug.LogWarning(
                    $"[Building] {buildingName}: default furniture slot for '{slot.ItemSO.name}' has no TargetRoom and the MainRoom has no FurnitureManager — slot will spawn under the building root without grid registration.",
                    this);
            }
        }
        catch
        {
            // Critical: a half-registered NetworkObject left in SpawnManager.SpawnedObjectsList
            // NRE's the next client-join scene-sync at NetworkObject.Serialize. Despawn + Destroy
            // on failure so the outer catch in TrySpawnDefaultFurniture only logs the exception
            // — it never leaves a corrupted entry in NGO's spawned list.
            if (spawned && netObj != null && netObj.IsSpawned)
            {
                try { netObj.Despawn(destroy: true); }
                catch (System.Exception cleanupEx) { Debug.LogException(cleanupEx, this); }
            }
            else if (instance != null)
            {
                Destroy(instance.gameObject);
            }
            throw;
        }
    }

    // =========================================================================
    // CONSTRUCTION FINALIZATION (server-only)
    // =========================================================================

    /// <summary>
    /// Server-only. Mirrors the formula tested in ConstructionProgressMathTests.
    /// Reads _constructionRequirements + _contributedMaterials and returns
    /// clamped sum(min(deliveredᵢ, requiredᵢ)) / sum(requiredᵢ).
    /// </summary>
    public float ComputeProgress()
    {
        if (_constructionRequirements == null || _constructionRequirements.Count == 0) return 1f;

        int totalRequired = 0;
        int totalSatisfied = 0;
        for (int i = 0; i < _constructionRequirements.Count; i++)
        {
            var req = _constructionRequirements[i];
            if (req.Item == null) continue;
            int r = req.Amount;
            int d = _contributedMaterials.TryGetValue(req.Item, out int v) ? v : 0;
            totalRequired += r;
            totalSatisfied += System.Math.Min(d, r);
        }
        if (totalRequired <= 0) return 1f;
        return Mathf.Clamp01((float)totalSatisfied / totalRequired);
    }

    /// <summary>
    /// Server-only. Atomic transition from UnderConstruction to Complete:
    /// flips the state (which fires HandleStateChanged → visual swap +
    /// TrySpawnDefaultFurniture + EvictLeftoversToPerimeter automatically).
    ///
    /// Idempotent: a second call when already Complete is a silent no-op.
    ///
    /// Called by CharacterAction_FinishConstruction.OnTick when progress hits 1.
    ///
    /// Note: shadows <see cref="object.Finalize"/> (the GC finalizer hook). Building
    /// has no destructor so the shadowing is intentional and harmless. The `new`
    /// keyword silences CS0114.
    /// </summary>
    public new void Finalize()
    {
        if (!IsServer) return;
        if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete) return;

        _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
        if (ConstructionProgress.Value < 1f) ConstructionProgress.Value = 1f;
        Debug.Log($"<color=green>[Building.Construction]</color> {buildingName} completed by Finalize().");
    }

    /// <summary>
    /// Owner/Client → Server relay. Asks the server to start
    /// <see cref="CharacterAction_FinishConstruction"/> against this building.
    ///
    /// Why an RPC: <see cref="CharacterAction_Continuous.OnTick"/> is server-authoritative
    /// (only the server runs OnTick; clients see effects via NetworkVariable replication).
    /// If a non-host client called <c>actor.CharacterActions.ExecuteAction(...)</c> locally,
    /// the resulting coroutine would tick forever without ever advancing — server has no
    /// idea the action exists. So clients dispatch through here; the server resolves the
    /// actor + validates ownership/state, then queues the action on the server-side
    /// <see cref="CharacterActions"/>. Visual proxy then replicates back to all peers via
    /// the existing <c>BroadcastActionVisualsClientRpc</c> path inside ExecuteAction.
    ///
    /// On host the RPC short-circuits to a direct method call in the same frame
    /// (no extra latency, NGO dispatch optimisation).
    ///
    /// Phase 1 cooperative model: no owner check — any character with the building
    /// in their interaction zone can drive the action. <see cref="BuildingInteractable.IsOwner"/>
    /// is reserved for Phase 2 (Abandon / Sell hold-menu options).
    /// </summary>
    // [ServerRpc(RequireOwnership = false)] is the rock-solid client→server path used by
    // the rest of the project. The Building NetworkObject is owned by the server, so any
    // client invoking this is by definition not the owner — RequireOwnership=false lets
    // them through. Method name MUST end in "ServerRpc" for the legacy attribute.
    [Unity.Netcode.ServerRpc(RequireOwnership = false)]
    public void RequestStartFinishConstructionServerRpc(Unity.Netcode.NetworkBehaviourReference actorRef)
    {
        if (!IsServer) { Debug.LogWarning($"[Building.SRpc] {buildingName} aborted — !IsServer"); return; }
        if (!IsUnderConstruction) return; // benign — zone-press race after Finalize.
        if (!actorRef.TryGet(out Character actor) || actor == null) { Debug.LogWarning($"[Building.SRpc] {buildingName} aborted — actorRef.TryGet failed"); return; }
        if (actor.CharacterActions == null) { Debug.LogWarning($"[Building.SRpc] {buildingName} aborted — actor.CharacterActions null"); return; }

        // Cooperative model: any character can finalize. Phase 1 owner-check removed.
        var action = new CharacterAction_FinishConstruction(actor, this);
        actor.CharacterActions.ExecuteAction(action);
    }

    /// <summary>
    /// Server-only. Restores construction state from a <see cref="MWI.WorldSystem.BuildingSaveData"/>
    /// snapshot. Called by <c>MapController.SpawnSavedBuildings</c> / <c>MapController.WakeUp</c>
    /// after the building's NetworkObject has been spawned and <see cref="OnNetworkSpawn"/> has run.
    ///
    /// Standalone runtime builds: the save path's <c>DeliveredMaterials</c> list is empty
    /// (AssetGuid resolution requires <c>AssetDatabase</c>, which is editor-only), so the
    /// restore is a UX hint — the next <c>ConstructionSiteScanner</c> tick reconciles against
    /// actual physical items on the footprint and rebuilds the meter from there.
    ///
    /// Editor builds: resolves each ItemSO by AssetGuid and replays
    /// <see cref="ContributeMaterial"/> so <c>_contributedMaterials</c> matches the saved
    /// snapshot.
    /// </summary>
    public void RestoreFromSaveData(MWI.WorldSystem.BuildingSaveData data)
    {
        if (!IsServer) return;
        if (data == null) return;

        // Restore the construction state. OnNetworkSpawn unconditionally re-derives state
        // from _constructionRequirements.Count and writes UnderConstruction for any prefab
        // that has requirements — that override has to be reverted here so a saved Complete
        // building doesn't load back as a scaffold. ContributeMaterial below also calls
        // CheckConstructionCompletion which could flip state to Complete, so we set the
        // state FIRST then let ContributeMaterial run on top.
        if (_currentState.Value != data.State) _currentState.Value = data.State;

        // Always restore the meter, even if DeliveredMaterials is empty — UX pre-warm.
        ConstructionProgress.Value = Mathf.Clamp01(data.ConstructionProgress);

#if UNITY_EDITOR
        if (data.DeliveredMaterials == null || data.DeliveredMaterials.Count == 0) return;

        // Resolve each ItemSO by AssetGuid and rebuild _contributedMaterials.
        foreach (var entry in data.DeliveredMaterials)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ItemAssetGuid) || entry.Delivered <= 0) continue;
            try
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(entry.ItemAssetGuid);
                if (string.IsNullOrEmpty(path)) continue;
                var so = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemSO>(path);
                if (so == null) continue;
                // ContributeMaterial bumps _contributedMaterials atomically.
                ContributeMaterial(so, entry.Delivered);
            }
            catch (System.Exception e) { Debug.LogException(e, this); }
        }
#endif
    }

    /// <summary>
    /// Returns the point on the AABB perimeter (vertical faces only — Y is preserved)
    /// nearest to `inside`, plus the outward face normal. Pure math; mirrors
    /// PerimeterMathTests.
    /// </summary>
    private static (Vector3 point, Vector3 normal) NearestPerimeterPoint(Bounds bounds, Vector3 inside)
    {
        float dxMin = inside.x - bounds.min.x;
        float dxMax = bounds.max.x - inside.x;
        float dzMin = inside.z - bounds.min.z;
        float dzMax = bounds.max.z - inside.z;

        float minDist = dxMin;
        Vector3 normal = Vector3.left;
        Vector3 face = new Vector3(bounds.min.x, inside.y, inside.z);

        if (dxMax < minDist) { minDist = dxMax; normal = Vector3.right;   face = new Vector3(bounds.max.x, inside.y, inside.z); }
        if (dzMin < minDist) { minDist = dzMin; normal = Vector3.back;    face = new Vector3(inside.x, inside.y, bounds.min.z); }
        if (dzMax < minDist) {                  normal = Vector3.forward; face = new Vector3(inside.x, inside.y, bounds.max.z); }

        return (face, normal);
    }

    /// <summary>
    /// Server-only. After Complete, evicts any remaining WorldItems on the footprint
    /// to just outside its perimeter so they don't clip into the finished building's
    /// interior. Snaps to NavMesh when possible, otherwise free-falls onto the eject
    /// point.
    /// </summary>
    private void EvictLeftoversToPerimeter()
    {
        if (!IsServer) return;
        if (_buildingZone == null) return;

        var leftovers = GetPhysicalItemsInCollider(_buildingZone);
        if (leftovers == null || leftovers.Count == 0) return;

        Bounds bounds = _buildingZone.bounds;
        foreach (var item in leftovers)
        {
            if (item == null || item.IsBeingCarried) continue;

            try
            {
                var (point, normal) = NearestPerimeterPoint(bounds, item.transform.position);
                Vector3 ejectPoint = point + normal * 0.5f;

                if (UnityEngine.AI.NavMesh.SamplePosition(ejectPoint, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
                    item.transform.position = hit.position;
                else
                    item.transform.position = ejectPoint;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }
    }
}
