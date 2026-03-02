using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(FurnitureGrid))]
[RequireComponent(typeof(FurnitureManager))]
public class Room : Zone
{
    [Header("Room Info")]
    [SerializeField] protected string _roomName;
    [SerializeField] protected List<Character> _roomOwners = new List<Character>();
    [SerializeField] protected List<Character> _roomResidents = new List<Character>();

    protected FurnitureManager _furnitureManager;

    public string RoomName => _roomName;
    public IReadOnlyList<Character> Owners => _roomOwners;
    public IReadOnlyList<Character> Residents => _roomResidents;
    
    public virtual bool IsResident(Character character)
    {
        return character != null && _roomResidents.Contains(character);
    }
    public FurnitureManager FurnitureManager => _furnitureManager;
    // Helper to get the grid from the manager for quick access
    public FurnitureGrid Grid => _furnitureManager != null ? _furnitureManager.Grid : null;

    protected override void Awake()
    {
        base.Awake();
        _furnitureManager = GetComponent<FurnitureManager>();
        
        // Ensure Grid is initialized by the Zone BoxCollider
        if (_boxCollider != null && _furnitureManager.Grid != null)
        {
            _furnitureManager.Grid.Initialize(_boxCollider);
        }
        else
        {
            Debug.LogError($"<color=red>[Room]</color> {_roomName} requires a BoxCollider to define its area and initialize the FurnitureGrid.");
        }

        // Load existing furniture in children through the manager
        _furnitureManager.LoadExistingFurniture();
    }



    /// <summary>
    /// Helper to verify if a point is inside the Room's BoxCollider bounds.
    /// </summary>
    public bool IsPointInsideRoom(Vector3 point)
    {
        if (_boxCollider == null) return false;
        return _boxCollider.bounds.Contains(point);
    }

    public void AddOwner(Character owner)
    {
        if (owner != null && !_roomOwners.Contains(owner))
        {
            _roomOwners.Add(owner);
        }
    }

    public void RemoveOwner(Character owner)
    {
        if (owner != null && _roomOwners.Contains(owner))
        {
            _roomOwners.Remove(owner);
        }
    }

    public virtual bool AddResident(Character resident)
    {
        if (resident != null && !_roomResidents.Contains(resident))
        {
            _roomResidents.Add(resident);
            return true;
        }
        return false;
    }

    public virtual bool RemoveResident(Character resident)
    {
        if (resident != null && _roomResidents.Contains(resident))
        {
            _roomResidents.Remove(resident);
            return true;
        }
        return false;
    }
}
