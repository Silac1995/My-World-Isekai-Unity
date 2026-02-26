using UnityEngine;

/// <summary>
/// Interactable pour les stations de craft (enclume, four, établi...).
/// Quand un personnage interagit, il s'installe à la station pour crafter.
/// </summary>
public class CraftingFurnitureInteractable : FurnitureInteractable
{
    [SerializeField] private CraftingStation _craftingStation;
    
    [Header("UI Reference")]
    [Tooltip("Le prefab de la fenêtre UI de craft. Il sera instancié au premier clic.")]
    [SerializeField] private GameObject _craftingWindowPrefab;

    private MWI.UI.Crafting.CraftingWindow _instantiatedWindow;

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

        // 1. Instancier la fenêtre si elle n'existe pas encore
        if (_instantiatedWindow == null && _craftingWindowPrefab != null)
        {
            // Trouver le parent UI où l'instancier
            GameObject canvasParent = GameObject.Find("UI_Player");
            
            GameObject windowGo;
            if (canvasParent != null)
            {
                windowGo = Object.Instantiate(_craftingWindowPrefab, canvasParent.transform);
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Crafting]</color> Canvas parent introuvable. Instanciation à la racine.");
                windowGo = Object.Instantiate(_craftingWindowPrefab);
            }

            _instantiatedWindow = windowGo.GetComponent<MWI.UI.Crafting.CraftingWindow>();
        }

        // 2. Ouvrir la fenêtre de craft
        if (_instantiatedWindow != null)
        {
            _instantiatedWindow.OpenForStation(this, user);
        }
        else
        {
            Debug.LogError($"<color=red>[Crafting]</color> Prefab de CraftingWindow invalide ou non assigné sur {name} !");
            Release(); // Libère le joueur s'il y a une erreur
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
