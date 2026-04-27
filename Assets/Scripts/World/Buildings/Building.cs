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
}
