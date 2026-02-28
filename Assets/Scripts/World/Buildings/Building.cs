using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Classe abstraite mère de tous les bâtiments.
/// Hérite de Zone pour bénéficier du trigger, du NavMesh sampling, et du tracking des personnages.
/// </summary>
public abstract class Building : Zone
{
    [Header("Building Info")]
    [SerializeField] protected string buildingName;

    // Pièces du bâtiment
    [SerializeField] private List<Room> _rooms = new List<Room>();

    public string BuildingName => buildingName;
    public abstract BuildingType BuildingType { get; }
    public IReadOnlyList<Room> Rooms => _rooms;

    protected override void Awake()
    {
        base.Awake();
        
        _rooms = new List<Room>(GetComponentsInChildren<Room>());
        
        if (_rooms.Count == 0)
        {
            Debug.LogError($"<color=red>[Building]</color> {buildingName} n'a aucune Room enfant. Un bâtiment doit avoir au moins une Room !");
        }
    }

    public Room GetRoomAt(Vector3 position)
    {
        foreach (var room in _rooms)
        {
            if (room.IsPointInsideRoom(position))
                return room;
        }
        return null;
    }

    public List<T> GetRoomsOfType<T>() where T : Room
    {
        return _rooms.OfType<T>().ToList();
    }

    /// <summary>
    /// Trouve le premier meuble disponible d'un type donné dans toutes les pièces du bâtiment.
    /// </summary>
    public T FindAvailableFurniture<T>() where T : Furniture
    {
        foreach (var room in _rooms)
        {
            T result = room.FindAvailableFurniture<T>();
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Récupère tous les meubles d'un type donné dans toutes les pièces du bâtiment.
    /// </summary>
    public List<T> GetFurnitureOfType<T>() where T : Furniture
    {
        List<T> result = new List<T>();
        foreach (var room in _rooms)
        {
            result.AddRange(room.Furnitures.OfType<T>());
        }
        return result;
    }
}
