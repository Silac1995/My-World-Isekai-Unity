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
    /// Si pas d'InteractionPoint défini, utilise la position du meuble.
    /// </summary>
    public Vector3 GetInteractionPosition()
    {
        return _interactionPoint != null ? _interactionPoint.position : transform.position;
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
    }

#if UNITY_EDITOR    
    private void OnDrawGizmosSelected()
    {
        // Dessine la zone occupée par le meuble selon _sizeInCells
        // On suppose que la FurnitureGrid utilise des cellules de 1x1.
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.4f); // Bleu semi-transparent
        
        // On dessine depuis l'origine (le coin inférieur gauche presumé ou le centre)
        // Pour être sûr, on dessine la taille exacte qu'il va réclamer à la grille.
        Vector3 size = new Vector3(_sizeInCells.x, 0.1f, _sizeInCells.y);
        Vector3 centerOffset = new Vector3(size.x / 2f, 0, size.z / 2f);
        
        // On trace le cube représentant la place prise sur le sol
        Gizmos.DrawCube(transform.position + centerOffset, size);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position + centerOffset, size);
    }
#endif
}
