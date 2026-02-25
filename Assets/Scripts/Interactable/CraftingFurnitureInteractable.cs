using UnityEngine;

/// <summary>
/// Interactable pour les stations de craft (enclume, four, établi...).
/// Quand un personnage interagit, il s'installe à la station pour crafter.
/// </summary>
public class CraftingFurnitureInteractable : FurnitureInteractable
{
    [SerializeField] private CraftingStation _craftingStation;

    public CraftingStation CraftingStation => _craftingStation;

    protected override void Awake()
    {
        base.Awake();
        if (_craftingStation == null)
            _craftingStation = GetComponent<CraftingStation>();
    }

    protected override void OnFurnitureUsed(Character user)
    {
        base.OnFurnitureUsed(user);

        // Positionner le personnage au point d'interaction
        Vector3 interactionPos = Furniture.GetInteractionPosition();
        user.CharacterMovement?.SetDestination(interactionPos);

        // Orienter le personnage vers la station
        user.CharacterVisual?.SetLookTarget(transform);

        Debug.Log($"<color=yellow>[Crafting]</color> {user.CharacterName} s'installe à {Furniture.FurnitureName} pour crafter.");

        // TEST : craft le premier item de la liste
        if (_craftingStation != null && _craftingStation.CraftableItems.Count > 0)
        {
            _craftingStation.Craft(_craftingStation.CraftableItems[0]);
            Release();
        }
    }

    public override void Release()
    {
        // Libérer le regard du personnage
        if (Furniture != null && Furniture.Occupant != null)
        {
            Furniture.Occupant.CharacterVisual?.ClearLookTarget();
        }

        base.Release();
    }
}
