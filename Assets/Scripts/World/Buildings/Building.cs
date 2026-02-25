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

    // Meubles détectés automatiquement dans les enfants du building
    private List<Furniture> _furniture = new List<Furniture>();

    public string BuildingName => buildingName;
    public abstract BuildingType BuildingType { get; }
    public IReadOnlyList<Furniture> Furniture => _furniture;

    protected override void Awake()
    {
        base.Awake();
        _furniture = new List<Furniture>(GetComponentsInChildren<Furniture>());
    }

    /// <summary>
    /// Trouve le premier meuble disponible d'un type donné.
    /// </summary>
    public T FindAvailableFurniture<T>() where T : Furniture
    {
        foreach (var f in _furniture)
        {
            if (f is T typed && !typed.IsOccupied)
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Récupère tous les meubles d'un type donné.
    /// </summary>
    public List<T> GetFurnitureOfType<T>() where T : Furniture
    {
        return _furniture.OfType<T>().ToList();
    }
}
