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
            foreach (Transform child in transform)
            {
                Room r = child.GetComponent<Room>();
                if (r != null)
                {
                    AddSubRoom(r);
                }
            }
        }
        
        if (_subRooms.Count == 0)
        {
            Debug.LogWarning($"<color=orange>[Building]</color> {buildingName} n'a aucune sous-Room enfant définie dans ses SubRooms.");
        }
    }
}
