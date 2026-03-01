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

    public string BuildingName => buildingName;
    public virtual BuildingType BuildingType => _buildingType;
    public bool IsPublicLocation => _isPublicLocation;

    public ComplexRoom MainRoom => this;

    // Use inherited GetAllRooms() to replace the old _rooms list logic
    public IReadOnlyList<Room> Rooms => GetAllRooms();

    protected override void Awake()
    {
        base.Awake(); // Will initialize Room and Zone logic (e.g., FurnitureGrid for the building envelope)
        
        // Auto-populate SubRooms if the user forgot to assign them in the inspector,
        // specifically searching for direct children rooms.
        if (_subRooms.Count == 0)
        {
            // On cherche TOUS les Room enfants, MAIS on exclut ce script lui-même (puisqu'un Building EST un Room)
            Room[] childRooms = GetComponentsInChildren<Room>();
            foreach (Room r in childRooms)
            {
                if (r != this)
                {
                    AddSubRoom(r);
                }
            }
        }
        
        // Si _subRooms.Count == 0, ce n'est pas grave, on considère que le Building est une pièce unique (Room).
    }

    protected virtual void Start()
    {
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
}
