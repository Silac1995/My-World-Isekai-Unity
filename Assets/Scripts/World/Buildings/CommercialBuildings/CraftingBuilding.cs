using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Bâtiment spécialisé dans l'artisanat (Forge, Menuiserie, Tissage, etc.).
/// Gère la collecte des recettes disponibles via les CraftingStations installées.
///
/// Implements <see cref="IStockProvider"/> so the <see cref="BuildingLogisticsManager"/>
/// can proactively restock input materials on every worker punch-in — independently
/// of whether any external <see cref="CraftingOrder"/> has been commissioned yet.
/// </summary>
public abstract class CraftingBuilding : CommercialBuilding, IStockProvider
{
    [Header("Crafting Input Stock Targets")]
    [Tooltip("Input materials this workshop wants to keep on hand at all times. " +
             "Each entry drives a BuyOrder when the virtual stock (physical + in-flight) " +
             "drops below MinStock. Authored in the Inspector per prefab.")]
    [SerializeField] private List<StockTarget> _inputStockTargets = new List<StockTarget>();

    /// <summary>Inspector-authored list of input materials to keep restocked.</summary>
    public IReadOnlyList<StockTarget> InputStockTargets => _inputStockTargets;

    /// <inheritdoc/>
    public IEnumerable<StockTarget> GetStockTargets()
    {
        foreach (var target in _inputStockTargets)
        {
            if (target.ItemToStock == null) continue;
            if (target.MinStock <= 0) continue;
            yield return target;
        }
    }

    /// <summary>
    /// Scanne toutes les pièces du bâtiment pour trouver les CraftingStations
    /// et retourne la liste consolidée de tous les objets pouvant y être fabriqués.
    /// </summary>
    public List<ItemSO> GetCraftableItems()
    {
        HashSet<ItemSO> uniqueItems = new HashSet<ItemSO>();

        // Parcourt toutes les pièces (y compris la MainRoom et ses SubRooms)
        foreach (var room in Rooms)
        {
            // Récupère toutes les CraftingStations de la pièce actuelle
            var stations = room.GetFurnitureOfType<CraftingStation>();

            foreach (var station in stations)
            {
                if (station.CraftableItems != null)
                {
                    foreach (var item in station.CraftableItems)
                    {
                        if (item != null)
                        {
                            uniqueItems.Add(item);
                        }
                    }
                }
            }
        }

        return uniqueItems.ToList();
    }

    /// <summary>
    /// Retourne toutes les stations de craft de ce bâtiment correspondant à un type précis.
    /// </summary>
    public IEnumerable<CraftingStation> GetStationsOfType(CraftingStationType type)
    {
        foreach (var room in Rooms)
        {
            foreach (var station in room.GetFurnitureOfType<CraftingStation>())
            {
                if (station.SupportsType(type))
                {
                    yield return station;
                }
            }
        }
    }

    // === Methodes Globales Fournisseur ===

    public override bool ProducesItem(ItemSO item)
    {
        return GetCraftableItems().Contains(item);
    }

    public override bool RequiresCraftingFor(ItemSO item)
    {
        // Les batiments d'artisanat fabriquent les items a la demande via des CraftingOrder
        return ProducesItem(item);
    }
}
