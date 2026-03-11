using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Station de craft (enclume, four, établi...).
/// Utilisée par les artisans pour fabriquer des objets.
/// Contient une liste d'items craftables et une méthode Craft pour les produire.
/// </summary>
public class CraftingStation : Furniture
{
    [Header("Crafting Station")]
    [SerializeField] private CraftingStationType _stationType;
    [SerializeField] private List<ItemSO> _craftableItems = new List<ItemSO>();
    [SerializeField] private Transform _outputPoint;

    public CraftingStationType StationType => _stationType;
    public IReadOnlyList<ItemSO> CraftableItems => _craftableItems;

    /// <summary>
    /// Vérifie si cette station peut être utilisée pour un type de craft donné.
    /// </summary>
    public bool SupportsType(CraftingStationType type)
    {
        return _stationType == type;
    }

    /// <summary>
    /// Vérifie si un item peut être crafté à cette station.
    /// </summary>
    public bool CanCraft(ItemSO item)
    {
        return item != null && _craftableItems.Contains(item);
    }

    /// <summary>
    /// Craft un item et le fait spawn dans le monde.
    /// L'item apparaît au _outputPoint (ou au-dessus de la station si non défini).
    /// </summary>
    public ItemInstance Craft(ItemSO item, Character crafter, bool isForWork = false, CommercialBuilding workPlace = null, Color primaryColor = default, Color secondaryColor = default)
    {
        if (item == null)
        {
            Debug.LogWarning($"<color=red>[Crafting]</color> Impossible de crafter : item null.");
            return null;
        }

        // Vérification d'occupation : seul l'occupant actuel (ou quelqu'un si c'est vide) peut crafter
        if (IsOccupied && Occupant != crafter)
        {
            Debug.LogWarning($"<color=red>[Crafting]</color> {crafter.CharacterName} ne peut pas utiliser {FurnitureName} car il est déjà utilisé par {Occupant.CharacterName}.");
            return null;
        }

        if (!CanCraft(item))
        {
            Debug.LogWarning($"<color=red>[Crafting]</color> {item.ItemName} ne peut pas être crafté à {FurnitureName}.");
            return null;
        }

        // Prefab world depuis le SO
        GameObject prefab = item.WorldItemPrefab;
        if (prefab == null)
        {
            Debug.LogError($"<color=red>[Crafting]</color> Pas de prefab sur le SO {item.ItemName} !");
            return null;
        }

        // Position de sortie
        Vector3 spawnPos = _outputPoint != null ? _outputPoint.position : transform.position + Vector3.up * 0.5f;

        // Si c'est pour le travail, on spawn l'objet dans la zone de stockage du bâtiment
        if (isForWork && workPlace != null && workPlace.StorageZone != null)
        {
            spawnPos = workPlace.StorageZone.GetRandomPointInZone();
        }

        // 1. Instancier le prefab WorldItem
        GameObject worldItemGo = Object.Instantiate(prefab, spawnPos, Quaternion.identity);
        worldItemGo.name = $"WorldItem_{item.ItemName}";

        // 2. Créer l'instance de données
        ItemInstance instance = item.CreateInstance();

        // Application des couleurs choisies
        if (primaryColor.a > 0f) instance.SetPrimaryColor(primaryColor);
        if (secondaryColor.a > 0f) instance.SetSecondaryColor(secondaryColor);

        // 3. Lier au WorldItem
        if (worldItemGo.TryGetComponent(out WorldItem worldItem))
        {
            worldItem.Initialize(instance);
        }
        else
        {
            Debug.LogError($"<color=red>[Crafting]</color> Le prefab n'a pas de composant WorldItem !");
            Object.Destroy(worldItemGo);
            return null;
        }

        Debug.Log($"<color=green>[Crafting]</color> {crafter.CharacterName} a crafté {item.ItemName} à {FurnitureName} !");
        return instance;
    }
}

public enum CraftingStationType
{
    Anvil,        // Enclume — armes, armures métalliques
    Furnace,      // Four/Fournaise — fondre les minerais
    Workbench,    // Établi — objets en bois, assemblage
    Loom,         // Métier à tisser — vêtements, tissus
    AlchemyTable  // Table d'alchimie — potions
}
