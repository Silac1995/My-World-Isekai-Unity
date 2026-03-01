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

        // Verify if the grid allows placement
        if (_grid.CanPlaceFurniture(targetPosition, furniturePrefab.SizeInCells))
        {
            Furniture newFurniture = Instantiate(furniturePrefab, targetPosition, Quaternion.identity, transform);
            
            // Correction du pivot Y : targetPosition correspond au sol (la grille).
            // Si le pivot du meuble est au centre de son volume, il sera à moitié enterré.
            Renderer[] renderers = newFurniture.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                // On calcule la bounding box de tout le meuble
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                // bounds.min.y correspond au point le plus bas (visuellement) du meuble dans le monde
                // On veut que ce point bas s'aligne exactement avec targetPosition.y
                float offsetY = newFurniture.transform.position.y - bounds.min.y;
                newFurniture.transform.position += new Vector3(0, offsetY, 0);
            }

            _furnitures.Add(newFurniture);
            _grid.RegisterFurniture(newFurniture, targetPosition, newFurniture.SizeInCells);
            Debug.Log($"<color=green>[Room]</color> Instanciation REUSSIE de {furniturePrefab.name} à {newFurniture.transform.position} dans {_roomName} !");
            return true;
        }

        Debug.LogWarning($"<color=orange>[Room]</color> Emplacement invalide ou déjà occupé pour le meuble {furniturePrefab.FurnitureName} à {targetPosition} dans {_roomName}.");
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
