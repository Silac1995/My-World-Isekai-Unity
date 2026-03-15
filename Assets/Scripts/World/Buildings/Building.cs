using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    public string BuildingName => buildingName;
    public virtual BuildingType BuildingType => _buildingType;
    public bool IsPublicLocation => _isPublicLocation;
    public Collider BuildingZone => _buildingZone;
    public Zone DeliveryZone => _deliveryZone;

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
    }

    protected virtual void Start()
    {
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

            Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
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
}
