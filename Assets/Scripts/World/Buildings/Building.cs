using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

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

    public string BuildingName => buildingName;
    public virtual BuildingType BuildingType => _buildingType;
    public bool IsPublicLocation => _isPublicLocation;
    public Collider BuildingZone => _buildingZone;
    public Zone DeliveryZone => _deliveryZone;
    
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
            float randomX = Random.Range(_buildingZone.bounds.min.x, _buildingZone.bounds.max.x);
            float randomZ = Random.Range(_buildingZone.bounds.min.z, _buildingZone.bounds.max.z);
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
