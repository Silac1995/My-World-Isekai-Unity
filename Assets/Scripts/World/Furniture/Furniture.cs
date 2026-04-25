using UnityEngine;

/// <summary>
/// Classe de base pour tous les meubles placés dans les buildings.
/// Un meuble a une position dans le monde, un occupant potentiel,
/// et un point d'interaction où le personnage doit se placer pour l'utiliser.
/// </summary>
public class Furniture : MonoBehaviour
{
    [Header("Furniture Info")]
    [SerializeField] private string _furnitureName;
    [SerializeField] private FurnitureTag _furnitureTag = FurnitureTag.None;
    [SerializeField] private Transform _interactionPoint;
    [SerializeField] private Vector2Int _sizeInCells = new Vector2Int(0, 0);

    [Header("Item Data")]
    [Tooltip("The FurnitureItemSO this furniture converts back to when picked up. Leave empty for non-pickable furniture.")]
    [SerializeField] private FurnitureItemSO _furnitureItemSO;

    public FurnitureItemSO FurnitureItemSO => _furnitureItemSO;

    private Character _occupant;
    private Character _reservedBy;
    private bool _sizeCalculated = false;

    public string FurnitureName => _furnitureName;
    public FurnitureTag FurnitureTag => _furnitureTag;
    public Transform InteractionPoint => _interactionPoint;
    public Vector2Int SizeInCells => _sizeInCells;
    public Character Occupant => _occupant;
    public Character ReservedBy => _reservedBy;
    public bool IsOccupied => _occupant != null;

    /// <summary>
    /// Réserve le meuble pour un personnage en approche.
    /// </summary>
    public virtual bool Reserve(Character character)
    {
        if (character == null) return false;
        if (IsOccupied || _reservedBy != null) return false;
        
        _reservedBy = character;
        return true;
    }

    /// <summary>
    /// Un personnage utilise physiquement ce meuble.
    /// </summary>
    public bool Use(Character character)
    {
        if (character == null) return false;
        if (IsOccupied)
        {
            Debug.Log($"<color=orange>[Furniture]</color> {_furnitureName} est déjà utilisé par {_occupant.CharacterName}.");
            return false;
        }

        _occupant = character;
        _reservedBy = null; // La réservation est convertie en occupation
        _occupant.SetOccupyingFurniture(this);
        Debug.Log($"<color=cyan>[Furniture]</color> {character.CharacterName} utilise {_furnitureName}.");
        return true;
    }

    /// <summary>
    /// Libère l'utilisation ou la réservation du meuble.
    /// </summary>
    public void Release()
    {
        if (_occupant != null)
        {
            Debug.Log($"<color=cyan>[Furniture]</color> {_occupant.CharacterName} quitte {_furnitureName}.");
            _occupant.SetOccupyingFurniture(null);
        }
        _occupant = null;
        _reservedBy = null;
    }

    /// <summary>
    /// Vérifie si le meuble est totalement libre (ni occupé, ni réservé).
    /// </summary>
    public virtual bool IsFree()
    {
        return _occupant == null && _reservedBy == null;
    }

    /// <summary>
    /// Position où le personnage doit se placer pour utiliser le meuble.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item>Authored <see cref="_interactionPoint"/> Transform — preferred and always wins.</item>
    ///   <item>The attached <see cref="FurnitureInteractable"/>'s <c>InteractionZone</c> bounds centre,
    ///         when present. Better than nothing because the InteractionZone is usually authored
    ///         to extend slightly beyond the furniture footprint, but for a zone shaped like the
    ///         furniture itself the centre is still inside the NavMeshObstacle carve. Prefer the
    ///         <see cref="GetInteractionPosition(Vector3)"/> overload — it returns the closest point
    ///         on the zone bounds to <c>fromPosition</c>, which lands on the navmesh-walkable face.</item>
    ///   <item>The furniture's own <c>transform.position</c> — last-resort, almost always inside the
    ///         carve. The 5-second softlock guard in <c>GoapAction_GatherStorageItems</c> exists
    ///         specifically for this case.</item>
    /// </list>
    /// </summary>
    public Vector3 GetInteractionPosition()
    {
        if (_interactionPoint != null) return _interactionPoint.position;

        var interactable = GetComponent<FurnitureInteractable>();
        if (interactable != null && interactable.InteractionZone != null)
        {
            return interactable.InteractionZone.bounds.center;
        }

        return transform.position;
    }

    /// <summary>
    /// Position-aware overload. When no <see cref="_interactionPoint"/> is authored but an
    /// <see cref="FurnitureInteractable"/> is present, returns the closest point on the
    /// interactable's <c>InteractionZone</c> bounds to <paramref name="fromPosition"/> —
    /// typically the worker's current location. This lands the target on the navmesh-walkable
    /// face of the zone instead of inside the obstacle carve.
    ///
    /// Use this overload from any AI / GOAP action that has the worker's transform available.
    /// </summary>
    public Vector3 GetInteractionPosition(Vector3 fromPosition)
    {
        if (_interactionPoint != null) return _interactionPoint.position;

        var interactable = GetComponent<FurnitureInteractable>();
        if (interactable != null && interactable.InteractionZone != null)
        {
            return interactable.InteractionZone.bounds.ClosestPoint(fromPosition);
        }

        return transform.position;
    }

    /// <summary>
    /// Authoring helper. Creates an `InteractionPoint` child placed in front of the
    /// furniture (along its local +Z axis) at a distance just outside the renderer
    /// bounds — so the spawned point is on walkable NavMesh, NOT inside the
    /// `NavMeshObstacle` carve from the base Furniture prefab. Workers walking to
    /// `GetInteractionPosition()` can then actually reach it.
    ///
    /// Without an interaction point, `GetInteractionPosition()` falls back to the
    /// furniture's `transform.position` (its centre), which is inside the carve —
    /// the worker will pathfind, never arrive within 1.5f, and the gather GOAP action
    /// will time out and fall back to the loose StorageZone drop.
    /// </summary>
    [ContextMenu("Auto Create Interaction Point")]
    private void AutoCreateInteractionPoint()
    {
        if (_interactionPoint != null)
        {
            Debug.LogWarning($"<color=orange>[Furniture]</color> {name} already has an _interactionPoint ({_interactionPoint.name}). Delete it first if you want to regenerate.");
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        float depth = 1f;
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (!(renderers[i] is ParticleSystemRenderer))
                    bounds.Encapsulate(renderers[i].bounds);
            }
            // Half the front-back footprint plus a 1u standoff so the worker stands
            // ~1u clear of the NavMeshObstacle carve.
            depth = (bounds.size.z * 0.5f) + 1f;
        }

        var go = new GameObject("InteractionPoint");
        go.transform.SetParent(transform, worldPositionStays: false);
        // Local +Z is "forward". Y stays at 0 so the point sits on the floor.
        go.transform.localPosition = new Vector3(0f, 0f, depth);
        go.transform.localRotation = Quaternion.identity;

        _interactionPoint = go.transform;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.EditorUtility.SetDirty(go);
#endif

        Debug.Log($"<color=cyan>[Furniture]</color> {name}: created InteractionPoint at local (0, 0, {depth:F2}). Verify it stands on walkable NavMesh in the Scene view.");
    }

    [ContextMenu("Auto Calculate Grid Size")]
    private void AutoCalculateSize()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"<color=orange>[Furniture]</color> {_furnitureName} n'a pas de renderers pour calculer une taille.");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (!(renderers[i] is ParticleSystemRenderer))
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        // On part du principe que la grille de base a des cellules de 1 unité (1m x 1m)
        float cellSize = 1f;

        // On arrondit toujours à l'entier supérieur pour que les bords ne dépassent jamais
        int widthCells = Mathf.CeilToInt(bounds.size.x / cellSize);
        int depthCells = Mathf.CeilToInt(bounds.size.z / cellSize);

        // Une sécurité min
        if (widthCells < 1) widthCells = 1;
        if (depthCells < 1) depthCells = 1;

        _sizeInCells = new Vector2Int(widthCells, depthCells);
        _sizeCalculated = true;
        // Debug.Log($"<color=cyan>[Furniture]</color> {_furnitureName} fait {_sizeInCells.x} x {_sizeInCells.y} cellules.");

        // Piggyback the interaction-point setup so authors don't have to remember it.
        // No-op when one is already assigned.
        if (_interactionPoint == null)
        {
            AutoCreateInteractionPoint();
        }
    }

    /// <summary>
    /// Editor-only: fires when the component is first added or right-clicked → Reset.
    /// Auto-creates an InteractionPoint child so newly-authored furniture is immediately
    /// usable by AI without a separate manual step.
    /// </summary>
    private void Reset()
    {
        if (_interactionPoint == null)
        {
            AutoCreateInteractionPoint();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_sizeInCells.x <= 0 || _sizeInCells.y <= 0) return;

        // Compute the actual mesh bounds center to align the gizmo with the visual
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Vector3 center;
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (!(renderers[i] is ParticleSystemRenderer))
                    bounds.Encapsulate(renderers[i].bounds);
            }
            // Use bounds center on X/Z, but place the gizmo at the bottom of the mesh on Y
            center = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
        else
        {
            center = transform.position;
        }

        Vector3 size = new Vector3(_sizeInCells.x, 0.1f, _sizeInCells.y);

        Gizmos.color = new Color(0f, 0.5f, 1f, 0.4f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(center, size);
    }
#endif
}
