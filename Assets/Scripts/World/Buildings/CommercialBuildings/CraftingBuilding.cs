using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Bâtiment spécialisé dans l'artisanat (Forge, Menuiserie, Tissage, etc.).
/// Gère la collecte des recettes disponibles via les CraftingStations installées.
/// </summary>
public abstract class CraftingBuilding : CommercialBuilding
{
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
