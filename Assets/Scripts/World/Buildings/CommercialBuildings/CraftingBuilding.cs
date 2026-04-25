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
    ///
    /// Robust to room-registration races: the canonical path walks <see cref="Building.Rooms"/>
    /// and queries each room's <c>FurnitureManager._furnitures</c> list. If that turns up empty
    /// (which can happen when the recent <c>_defaultFurnitureLayout</c>/<see cref="CommercialBuilding.SpawnDefaultFurnitureSlot"/>
    /// flow spawns a CraftingStation but its <c>RegisterSpawnedFurnitureUnchecked</c> didn't land
    /// — slot.TargetRoom unset, FurnitureGrid null on the target room, OnNetworkSpawn race, etc.)
    /// we fall back to a transform-tree scan so the building's logical capability still reflects
    /// the stations physically attached to it. This is the difference between
    /// "FindSupplierFor(tshirt) returns null and the shop can never order tshirts" vs.
    /// "logistics works, with a one-shot warning the user can fix at their own pace".
    /// </summary>
    public List<ItemSO> GetCraftableItems()
    {
        HashSet<ItemSO> uniqueItems = new HashSet<ItemSO>();
        HashSet<CraftingStation> registeredStations = new HashSet<CraftingStation>();

        // Primary path — walk every room's _furnitures list, recursive across MainRoom + every
        // SubRoom (Building.Rooms uses ComplexRoom.GetAllRooms()). Track which stations we found
        // so the fallback can detect stations the room-registration path missed *partially* (e.g.
        // MainRoom registered correctly but a SubRoom didn't).
        foreach (var room in Rooms)
        {
            foreach (var station in room.GetFurnitureOfType<CraftingStation>())
            {
                if (station == null) continue;
                registeredStations.Add(station);
                if (station.CraftableItems == null) continue;
                foreach (var item in station.CraftableItems)
                {
                    if (item != null) uniqueItems.Add(item);
                }
            }
        }

        // Fallback / supplement — physical scan of every CraftingStation under this building's
        // transform tree (covers stations under the building root, MainRoom, AND every SubRoom).
        // Catches stations spawned via TrySpawnDefaultFurniture / RegisterSpawnedFurnitureUnchecked
        // that didn't make it into a room's _furnitures list — slot.TargetRoom unset, FurnitureGrid
        // null on the target room, mid-OnNetworkSpawn timing race, or stations parented under the
        // building root because SpawnDefaultFurnitureSlot intentionally avoids reparenting under
        // a Room (Room sits on a non-NetworkObject GameObject and reparenting would throw
        // InvalidParentException). Without this supplement, the shop's logistics manager would
        // never find a supplier for items those stations produce.
        //
        // Important: we run this even when the primary path found stations, because partial
        // registration (some rooms registered, some not) is a real failure mode. We only emit
        // the diagnostic warning for stations the primary path actually missed.
        var childStations = GetComponentsInChildren<CraftingStation>(includeInactive: true);
        if (childStations != null && childStations.Length > 0)
        {
            int unregisteredCount = 0;
            foreach (var station in childStations)
            {
                if (station == null) continue;
                if (registeredStations.Contains(station)) continue; // already counted via primary
                unregisteredCount++;
                if (station.CraftableItems == null) continue;
                foreach (var item in station.CraftableItems)
                {
                    if (item != null) uniqueItems.Add(item);
                }
            }

            if (unregisteredCount > 0)
            {
                Debug.LogWarning(
                    $"[CraftingBuilding] {buildingName}: {unregisteredCount} CraftingStation(s) found in transform tree " +
                    $"but missing from any Room.FurnitureManager._furnitures list. Falling back to physical scan for them. " +
                    $"This usually means a default-furniture slot's RegisterSpawnedFurnitureUnchecked didn't run on the right " +
                    $"room (check each slot.TargetRoom is assigned and the target Room has a FurnitureGrid). " +
                    $"Building still produces those items, but FindAvailableFurniture / interaction zones may behave " +
                    $"unexpectedly until the registration is fixed.",
                    this);
            }
        }

        return uniqueItems.ToList();
    }

    /// <summary>
    /// Retourne toutes les stations de craft de ce bâtiment correspondant à un type précis.
    /// Mirrors the room-list-then-physical-scan pattern of <see cref="GetCraftableItems"/> so
    /// the worker can still pick a station even if registration into the room's _furnitures
    /// list failed (default-furniture spawn race).
    /// </summary>
    /// <summary>
    /// Enumerates every <see cref="CraftingStation"/> that physically belongs to this building,
    /// using the same primary-then-fallback pattern as <see cref="GetCraftableItems"/>: walks
    /// every Room's _furnitures list (covers MainRoom + all SubRooms recursively), then
    /// supplements with a transform-tree scan to catch stations that didn't make it into a
    /// room's registration (default-furniture spawn race, slot.TargetRoom unset, etc.).
    /// Deduped: a station registered in a Room AND found via transform scan only appears once.
    ///
    /// Use this from any consumer that picks "an available station" (e.g. <c>JobBlacksmith</c>'s
    /// <c>HandleSearchOrder</c>) — iterating <c>cb.Rooms</c> directly skips unregistered stations
    /// even when <see cref="ProducesItem"/> says the building does produce the requested item,
    /// which manifests as the worker getting stuck in "Searching for orders" while ingredients
    /// and orders are both ready.
    /// </summary>
    public IEnumerable<CraftingStation> GetAllStations()
    {
        var seen = new HashSet<CraftingStation>();

        foreach (var room in Rooms)
        {
            foreach (var station in room.GetFurnitureOfType<CraftingStation>())
            {
                if (station == null) continue;
                if (seen.Add(station)) yield return station;
            }
        }

        var childStations = GetComponentsInChildren<CraftingStation>(includeInactive: true);
        if (childStations != null)
        {
            foreach (var station in childStations)
            {
                if (station == null) continue;
                if (seen.Add(station)) yield return station;
            }
        }
    }

    public IEnumerable<CraftingStation> GetStationsOfType(CraftingStationType type)
    {
        // Yield from both paths but dedupe — a station registered into a Room's _furnitures
        // list AND found via transform-tree scan must only be yielded once. Yields from the
        // registered (room) path first because it's the canonical channel; physical-scan
        // entries supplement only what the primary path missed (could be a MainRoom-registered
        // case missing a SubRoom-unregistered station, or no rooms registered at all).
        var seen = new HashSet<CraftingStation>();

        // Primary: walks Rooms recursively (MainRoom + every SubRoom).
        foreach (var room in Rooms)
        {
            foreach (var station in room.GetFurnitureOfType<CraftingStation>())
            {
                if (station == null || !station.SupportsType(type)) continue;
                if (seen.Add(station)) yield return station;
            }
        }

        // Supplement: physical-scan fallback. See GetCraftableItems for the full rationale.
        var childStations = GetComponentsInChildren<CraftingStation>(includeInactive: true);
        if (childStations != null)
        {
            foreach (var station in childStations)
            {
                if (station == null || !station.SupportsType(type)) continue;
                if (seen.Add(station)) yield return station;
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
