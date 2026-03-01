using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(FurnitureGrid))]
public class Room : Zone
{
    [Header("Room Info")]
    [SerializeField] protected string _roomName;
    [SerializeField] protected List<Character> _owners = new List<Character>();

    [SerializeField] protected List<Furniture> _furnitures = new List<Furniture>();
    protected FurnitureGrid _grid;

    public string RoomName => _roomName;
    public IReadOnlyList<Character> Owners => _owners;
    public IReadOnlyList<Furniture> Furnitures => _furnitures;
    public FurnitureGrid Grid => _grid;

    protected override void Awake()
    {
        base.Awake();
        _grid = GetComponent<FurnitureGrid>();

        // Initialize the grid using the Room's BoxCollider (Zone trigger)
        if (_boxCollider != null)
        {
            _grid.Initialize(_boxCollider);
        }
        else
        {
            Debug.LogError($"<color=red>[Room]</color> {_roomName} requires a BoxCollider to define its area and initialize the FurnitureGrid.");
        }

        // Load existing furniture in children
        _furnitures = new List<Furniture>(GetComponentsInChildren<Furniture>());
        foreach (var f in _furnitures)
        {
            _grid.RegisterFurniture(f, f.transform.position, f.SizeInCells);
        }
    }

    /// <summary>
    /// Try adding a furniture to this room.
    /// </summary>
    public bool AddFurniture(Furniture furniturePrefab, Vector3 targetPosition)
    {
        if (_grid == null) return false;

        // Verify if it's within the room's bounds and not occupied on the grid
        if (IsPointInsideRoom(targetPosition) && _grid.CanPlaceFurniture(targetPosition, furniturePrefab.SizeInCells))
        {
            Furniture newFurniture = Instantiate(furniturePrefab, targetPosition, Quaternion.identity, transform);
            
            _furnitures.Add(newFurniture);
            _grid.RegisterFurniture(newFurniture, targetPosition, newFurniture.SizeInCells);
            return true;
        }

        Debug.LogWarning($"<color=orange>[Room]</color> Emplacement invalide ou déjà occupé pour le meuble {furniturePrefab.FurnitureName} dans {_roomName}.");
        return false;
    }

    /// <summary>
    /// Removes a furniture from this room.
    /// </summary>
    public void RemoveFurniture(Furniture furnitureToRemove)
    {
        if (_furnitures.Contains(furnitureToRemove))
        {
            _furnitures.Remove(furnitureToRemove);
            if (_grid != null)
            {
                _grid.UnregisterFurniture(furnitureToRemove);
            }
            Destroy(furnitureToRemove.gameObject);
        }
    }

    /// <summary>
    /// Find available furniture of type T.
    /// </summary>
    public T FindAvailableFurniture<T>() where T : Furniture
    {
        foreach (var f in _furnitures)
        {
            if (f is T typed && !typed.IsOccupied)
                return typed;
        }
        return null;
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
        if (owner != null && !_owners.Contains(owner))
        {
            _owners.Add(owner);
        }
    }

    public void RemoveOwner(Character owner)
    {
        if (owner != null && _owners.Contains(owner))
        {
            _owners.Remove(owner);
        }
    }
}
