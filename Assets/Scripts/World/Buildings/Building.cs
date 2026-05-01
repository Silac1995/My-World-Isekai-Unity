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

        // Initialize state based on requirements (only on server)
        if (IsServer)
        {
            if (_constructionRequirements != null && _constructionRequirements.Count > 0)
            {
                _currentState.Value = MWI.WorldSystem.BuildingState.UnderConstruction;
            }
            else
            {
                _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

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

        ConfigureNavMeshObstacles();

        // Server-only: spawn any _defaultFurnitureLayout entries (manual + auto-converted)
        // that don't already have a matching restored Furniture child. Hoisted from
        // CommercialBuilding 2026-05-01 so every Building subclass benefits.
        if (IsServer)
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
    }

    private void HandleStateChanged(MWI.WorldSystem.BuildingState previousValue, MWI.WorldSystem.BuildingState newValue)
    {
        if (newValue == MWI.WorldSystem.BuildingState.Complete)
        {
            OnConstructionComplete?.Invoke();

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

    protected virtual void OnDestroy()
    {
        if (BuildingManager.Instance != null)
        {
            BuildingManager.Instance.UnregisterBuilding(this);
        }
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

    /// <summary>
    /// Retourne tous les WorldItem posés physiquement dans la zone spécifiée.
    /// Utile pour inspecter les StorageZone, DepositZone, DeliveryZone.
    /// </summary>
    public List<WorldItem> GetPhysicalItemsInZone(Zone zone)
    {
        List<WorldItem> items = new List<WorldItem>();
        if (zone == null) return items;

        BoxCollider boxCol = zone.GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
            Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

            Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
            foreach (var col in colliders)
            {
                var worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
                
                // On s'assure que l'item n'est pas deja porte par quelqu'un d'autre
                if (worldItem != null && !worldItem.IsBeingCarried && !items.Contains(worldItem))
                {
                    items.Add(worldItem);
                }
            }
        }
        return items;
    }

    /// <summary>
    /// Forcibly builds the building instantly, bypassing all material requirements.
    /// </summary>
    public virtual void BuildInstantly()
    {
        if (!IsServer) return; // Modification of state must happen on server
        if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete) return;

        _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
        _contributedMaterials.Clear();
        Debug.Log($"<color=green>[Building]</color> {buildingName} has been built instantly.");
        // OnConstructionComplete will be triggered by state change callback
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
    ///   2. <see cref="UnityEngine.Object.Destroy(UnityEngine.Object)"/>'s the child
    ///      GameObject so NGO never sees it as a nested NetworkObject.
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
                Destroy(furniture.gameObject);
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

            // Dedup against existing serialized slots. Converted child wins.
            int existingIndex = _defaultFurnitureLayout.FindIndex(s => s != null && s.ItemSO == slot.ItemSO);
            if (existingIndex >= 0)
            {
                Debug.Log(
                    $"<color=cyan>[Building]</color> {buildingName}: nested child '{furniture.name}' overrides existing manual _defaultFurnitureLayout entry [{existingIndex}] for ItemSO '{slot.ItemSO.name}'. Remove the manual slot to silence this log.",
                    this);
                _defaultFurnitureLayout[existingIndex] = slot;
            }
            else
            {
                _defaultFurnitureLayout.Add(slot);
            }

            Destroy(furniture.gameObject);
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
        if (netObj != null && !netObj.IsSpawned)
        {
            netObj.Spawn();
        }

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
        if (slot.TargetRoom != null && slot.TargetRoom.FurnitureManager != null)
        {
            slot.TargetRoom.FurnitureManager.RegisterSpawnedFurnitureUnchecked(instance, worldPos);
        }
        else if (slot.TargetRoom == null)
        {
            Debug.LogWarning(
                $"[Building] {buildingName}: default furniture slot for '{slot.ItemSO.name}' has no TargetRoom. " +
                $"Set TargetRoom on the slot so it appears in the room's FurnitureManager list.",
                this);
        }
        else
        {
            // TargetRoom != null but its FurnitureManager is null — misconfiguration.
            Debug.LogWarning(
                $"[Building] {buildingName}: default furniture slot for '{slot.ItemSO.name}' targets Room '{slot.TargetRoom.name}' but that Room has no FurnitureManager — slot will spawn under the building root without grid registration.",
                this);
        }
    }
}
